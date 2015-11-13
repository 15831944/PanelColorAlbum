﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OfficeOpenXml;

namespace AlbumPanelColorTiles.PanelLibrary
{
   public static class SaveChangesToExel
   {

      // Сохранение изменений в файл Excel
      public static void Save(List<PanelAKR> panelsAkrInLib)
      {
         // файл
         string fileXls = PanelLibrarySaveService.LibPanelsExcelFilePath;
         if (!File.Exists(fileXls))
         {
            createExcel(fileXls);
         }
         try
         {
            using (var xlPackage = new ExcelPackage(new FileInfo(fileXls)))
            {               
               var worksheet = xlPackage.Workbook.Worksheets[1];
               int row = 2;

               while (worksheet.Cells[row, 1].Text != "")                                 
                  row++;
                              
               foreach (var panel in panelsAkrInLib)
               {
                  worksheet.Cells[row, 1].Value = panel.BlNameInLib;
                  worksheet.Cells[row, 2].Value = DateTime.Now;
                  worksheet.Cells[row, 3].Value = Environment.UserName;
                  worksheet.Cells[row, 4].Value = panel.ReportStatus;
                  worksheet.Cells[row, 5].Value = panel.IdBtrAkrPanelInLib.Database.Filename;
                  row++;
               }
               xlPackage.Save();
            }
         }
         catch (Exception ex)
         {
            Log.Error(ex, "Не удалось сохранить изменения библиотеки в Excel {0}", fileXls);
         }
      }

      private static void createExcel(string fileXls)
      {
         try
         {
            using (var xlPackage = new ExcelPackage(new FileInfo(fileXls)))
            {
               var worksheet = xlPackage.Workbook.Worksheets.Add("Изменения");
               worksheet.Cells[1, 1].Value = "АКР-Панель";
               worksheet.Cells[1, 2].Value = "Дата";
               worksheet.Cells[1, 3].Value = "Пользователь";
               worksheet.Cells[1, 4].Value = "Статус";
               worksheet.Cells[1, 5].Value = "Чертеж";
               xlPackage.Save();
            }
         }
         catch (Exception ex)
         {
            Log.Error(ex, "Не удалось создать файл Excel {0}", fileXls);
         }
      }
   }
}
