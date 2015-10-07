﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlbumPanelColorTiles.Lib;
using AlbumPanelColorTiles.Model;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace AlbumPanelColorTiles.Sheets
{
   // Листы содержания
   public class SheetsContent
   {
      private readonly int _countSheetsBeforContent = 2;

      // кол листов до содержания
      private readonly int _firstRowInTableForSheets = 2;

      private Album _album;
      private BlockFrameFacade _blFrameInFacade;
      private int _countContentSheets;
      private Database _dbContent;
      private Database _dbFacade;
      private List<SheetMarkSB> _sheetsMarkSB;
      private SheetsSet _sheetsSet;

      public SheetsContent(SheetsSet sheetsSet, BlockFrameFacade blFrameInFacade)
      {
         _sheetsSet = sheetsSet;
         _album = sheetsSet.Album;
         _sheetsMarkSB = sheetsSet.SheetsMarkSB;
         _blFrameInFacade = blFrameInFacade;
         Contents();
      }

      // Определение кол листов содержания по кол марок Ар и кол строк в таблице содержания на одном листе.
      private static int CalcSheetsContentNumber(int rowsTable, int marksAr)
      {
         int res = 0; // Нужное кол листов содержания
         int rowsToSheetsInTable = rowsTable - 2; // строк в таблице под листы
         int rowsInFirstSheet = rowsToSheetsInTable - 5;// строк под листы марки АР на первом листе содерж (без облажки тит и т.п )
         int numSheetsMarksAr = marksAr * 2; // кол листов марок АР (листы фасада и форм)
         numSheetsMarksAr -= rowsInFirstSheet; // вычитаем листы первого листа содержания
         res = 1;
         res += numSheetsMarksAr / rowsToSheetsInTable; // целое кол листов
         var remaindSheets = numSheetsMarksAr % rowsToSheetsInTable; // остаток листов
         if (remaindSheets > 0)
         {
            res++;
         }
         return res;
      }

      // Определение кол-ва марок АР
      private int CalcMarksArNumber(List<SheetMarkSB> marksSB)
      {
         return marksSB.Sum(sb => sb.SheetsMarkAR.Count);
      }

      private void CheckEndOfTable(Transaction t, ref int curContentLayout, ref Table tableContent, ref BlockReference blRefStamp, ref int row)
      {
         if (row == tableContent.Rows.Count)
         {
            // Новый лист содержания
            tableContent.RecomputeTableBlock(true);
            CopyContentSheet(t, ++curContentLayout, out tableContent, out blRefStamp);
            FillingStampContent(blRefStamp, curContentLayout, t);
            row = _firstRowInTableForSheets;
         }
      }

      // Содержание тома (Общие данные. Ведомость комплекта чертежей.)
      private void Contents()
      {
         // Создание файла содержания и титульных листов
         string fileContent = Path.Combine(_album.AlbumDir, "00_Содержание" + _album.AbbreviateProject + ".dwg");
         File.Copy(_sheetsSet.SheetTemplateFileContent, fileContent);

         // Кол листов содержания = Суммарное кол лисчтов панелей / на кол строк в таблице на одном листе
         // Но на первом листе содержания первые 5 строк заняты (обложка, тит, общие данные, наруж стен панели, том1).
         _dbFacade = HostApplicationServices.WorkingDatabase;
         using (_dbContent = new Database(false, true))
         {
            _dbContent.ReadDwgFile(fileContent, FileShare.ReadWrite, false, "");
            _dbContent.CloseInput(true);

            // Замена блока рамки из чертежа фасада, если он есть
            _blFrameInFacade.ChangeBlockFrame(_dbContent, Album.Options.BlockFrameName);

            using (var t = _dbContent.TransactionManager.StartTransaction())
            {
               int curContentLayout = 1;
               Table tableContent;
               BlockReference blRefStamp;
               CopyContentSheet(t, curContentLayout, out tableContent, out blRefStamp);
               // Определение кол-ва марок АР
               int countMarkArs = CalcMarksArNumber(_sheetsMarkSB);
               // Определение кол-ва листов содержания (только для панелей без учета лтстов обложек и тп.)
               _countContentSheets = CalcSheetsContentNumber(tableContent.Rows.Count, countMarkArs);
               // Заполнение штампа на первом листе содержания
               FillingStampContent(blRefStamp, curContentLayout, t);
               // текущая строка для записи листа
               int row = _firstRowInTableForSheets;
               // На первом листе содержания заполняем строки для Обложки, Тит, Общ дан, НСП, Том1.
               tableContent.Cells[row++, 1].TextString = "Обложка";
               tableContent.Cells[row++, 1].TextString = "Титульный лист";
               tableContent.Cells[row, 1].TextString = "Общие данные. Ведомость комплекта чертежей";
               tableContent.Cells[row, 2].TextString = _countContentSheets > 1 ? "3-" + (3 + _countContentSheets - 1).ToString() : "3";
               tableContent.Cells[row, 2].Alignment = CellAlignment.MiddleCenter;
               row++;
               tableContent.Cells[row++, 1].TextString = "Наружные стеновые панели";
               tableContent.Cells[row++, 1].TextString = "ТОМ";

               int curSheetArNum = _countContentSheets + _countSheetsBeforContent;// номер для первого листа Марки АР

               ProgressMeter progressMeter = new ProgressMeter();
               progressMeter.SetLimit(_sheetsMarkSB.Count);
               progressMeter.Start("Создание содержания ");

               foreach (var sheetMarkSB in _sheetsMarkSB)
               {
                  progressMeter.MeterProgress();
                  foreach (var sheetMarkAR in sheetMarkSB.SheetsMarkAR)
                  {
                     tableContent.Cells[row, 1].TextString = sheetMarkAR.MarkArDocumentation + ".Раскладка плитки на фасаде";
                     sheetMarkAR.SheetNumber = ++curSheetArNum;
                     tableContent.Cells[row, 2].TextString = curSheetArNum.ToString();
                     tableContent.Cells[row, 2].Alignment = CellAlignment.MiddleCenter;
                     row++;
                     CheckEndOfTable(t, ref curContentLayout, ref tableContent, ref blRefStamp, ref row);

                     tableContent.Cells[row, 1].TextString = sheetMarkAR.MarkArDocumentation + ".Раскладка плитки в форме";
                     tableContent.Cells[row, 2].TextString = curSheetArNum.ToString() + ".1";
                     sheetMarkAR.SheetNumberInForm = curSheetArNum.ToString() + ".1";
                     tableContent.Cells[row, 2].Alignment = CellAlignment.MiddleCenter;
                     row++;
                     CheckEndOfTable(t, ref curContentLayout, ref tableContent, ref blRefStamp, ref row);
                  }
               }
               progressMeter.Stop();

               //Удаление пустых строк в таблице содержания
               if (tableContent.Rows.Count > row + 1)
               {
                  tableContent.DeleteRows(row, tableContent.Rows.Count - row);
               }

               // Удаление последнего листа содержания (пустой копии)
               HostApplicationServices.WorkingDatabase = _dbContent;
               LayoutManager lm = LayoutManager.Current;
               string layoutNameToDel = (_countSheetsBeforContent + (++_countContentSheets)).ToString("00") + "_" + Album.Options.SheetTemplateLayoutNameForContent;
               lm.DeleteLayout(layoutNameToDel);

               t.Commit();
            }
            HostApplicationServices.WorkingDatabase = _dbFacade;
            _dbContent.SaveAs(fileContent, DwgVersion.Current);
         }
      }

      private void CopyContentSheet(Transaction t, int curContentLayout, out Table tableContent, out BlockReference blRefStamp)
      {
         Layout layout = GetCurLayoutContentAndCopyNext(curContentLayout, t);
         var btrLayoutContent = t.GetObject(layout.BlockTableRecordId, OpenMode.ForRead) as BlockTableRecord;
         tableContent = FindTableContent(btrLayoutContent, t);
         blRefStamp = FindBlRefStampContent(btrLayoutContent, t);
      }

      /// <summary>
      /// Заполнение штампа содержания.
      /// </summary>
      /// <param name="blRefStamp"></param>
      /// <param name="curContentLayout">Текущий лист содержания (без тит и обложек)</param>
      /// <param name="t"></param>
      private void FillingStampContent(BlockReference blRefStamp, int curContentLayout, Transaction t)
      {
         var atrs = blRefStamp.AttributeCollection;
         foreach (ObjectId idAtrRef in atrs)
         {
            if (idAtrRef.IsErased) continue;
            var atrRef = t.GetObject(idAtrRef, OpenMode.ForRead) as AttributeReference;
            string text = string.Empty;
            if (atrRef.Tag.Equals("ВИД", StringComparison.OrdinalIgnoreCase))
            {
               if (curContentLayout == 1 & _countContentSheets > 1)
                  text = "Общие данные. Ведомость комплекта чертежей (начало)";
               else if (curContentLayout == 1 & _countContentSheets == 1)
                  text = "Общие данные. Ведомость комплекта чертежей.";
               else if (curContentLayout == _countContentSheets)
                  text = "Общие данные. Ведомость комплекта чертежей (окончание)";
               else
                  text = "Общие данные. Ведомость комплекта чертежей (продолжение)";
            }
            else if (atrRef.Tag.Equals("Лист", StringComparison.OrdinalIgnoreCase))
            {
               text = (_countSheetsBeforContent + curContentLayout).ToString();
            }
            if (text != string.Empty)
            {
               atrRef.UpgradeOpen();
               atrRef.TextString = text;
            }
         }
      }

      private BlockReference FindBlRefStampContent(BlockTableRecord btrLayoutContent, Transaction t)
      {
         foreach (ObjectId idEnt in btrLayoutContent)
         {
            if (idEnt.ObjectClass.Name == "AcDbBlockReference")
            {
               var blRefStampContent = t.GetObject(idEnt, OpenMode.ForRead, false, true) as BlockReference;
               if (Blocks.EffectiveName(blRefStampContent).Equals(Album.Options.BlockFrameName, StringComparison.OrdinalIgnoreCase))
               {
                  return blRefStampContent;
               }
            }
         }
         throw new System.Exception("Не найден блок штампа в шаблоне содержания.");
      }

      private Table FindTableContent(BlockTableRecord btrLayoutContent, Transaction t)
      {
         // Поиск таблицы
         foreach (ObjectId idEnt in btrLayoutContent)
         {
            if (idEnt.ObjectClass.Name == "AcDbTable")
            {
               return t.GetObject(idEnt, OpenMode.ForWrite) as Table;
            }
         }
         throw new System.Exception("Не найдена заготовка таблицы в шаблоне содержания.");
      }

      private Layout GetCurLayoutContentAndCopyNext(int curSheetContentNum, Transaction t)
      {
         ObjectId idLayoutContentCur;
         HostApplicationServices.WorkingDatabase = _dbContent;
         LayoutManager lm = LayoutManager.Current;
         string nameLay;
         string nameCopy;
         if (curSheetContentNum == 1)
         {
            nameLay = Album.Options.SheetTemplateLayoutNameForContent;
            nameCopy = (_countSheetsBeforContent + 2).ToString("00") + "_" + Album.Options.SheetTemplateLayoutNameForContent;
            idLayoutContentCur = lm.GetLayoutId(nameLay);
            lm.CopyLayout(nameLay, nameCopy);
            lm.RenameLayout(nameLay, (_countSheetsBeforContent + 1).ToString("00") + "_" + Album.Options.SheetTemplateLayoutNameForContent);
         }
         else
         {
            nameLay = (_countSheetsBeforContent + curSheetContentNum).ToString("00") + "_" + Album.Options.SheetTemplateLayoutNameForContent;
            nameCopy = (_countSheetsBeforContent + (++curSheetContentNum)).ToString("00") + "_" + Album.Options.SheetTemplateLayoutNameForContent;
            idLayoutContentCur = lm.GetLayoutId(nameLay);
            lm.CopyLayout(nameLay, nameCopy);
         }
         return t.GetObject(idLayoutContentCur, OpenMode.ForRead) as Layout;
      }
   }
}