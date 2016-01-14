﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlbumPanelColorTiles.Options;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Xml.Serialization;
using AlbumPanelColorTiles.PanelLibrary;

namespace AlbumPanelColorTiles.Model.Base
{
   public class PanelBase
   {
      private string _markWithoutElectric;
      
      public BaseService Service { get; private set; }      
      public string BlNameAkr { get; set; }
      public string WindowsPrefix { get; set; }
      public List<Extents3d> Openings { get; set; }      
      public ObjectId IdBtrPanel { get; set; }
      public Panel Panel { get; private set; }
      public Dictionary<Point3d, string> WindowsBase { get; set; } = new Dictionary<Point3d, string>();

      public PanelBase(Panel panelXml, BaseService service)
      {
         Panel = panelXml;
         Service = service;
      }

      public string MarkWithoutElectric
      {
         get
         {
            if (string.IsNullOrEmpty(_markWithoutElectric))
            {
               _markWithoutElectric = MountingPanel.GetMarkWithoutElectric(Panel.mark);
            }
            return _markWithoutElectric;
         }
      }

      /// <summary>
      /// Создание определения блока панели по описанию из базы XML от конструкторов.
      /// Должна быть открыта транзакция.
      /// </summary>
      /// <exception cref="Autodesk.AutoCAD.Runtime.Exception">DuplicateBlockName</exception>
      /// <returns>ObjectId созданного определения блока в текущей базе.</returns>            
      public void CreateBlock()
      {         
         Openings = new List<Extents3d>();         
         Database db = HostApplicationServices.WorkingDatabase;
         Transaction t = db.TransactionManager.TopTransaction;
         BlockTable bt = db.BlockTableId.GetObject(OpenMode.ForWrite) as BlockTable;
         // Имя для блока панели АКР
         // Пока без "Щечек" и без окон

         BlNameAkr = defineBlockPanelAkrName();

         // Ошибка если блок с таким именем уже есть
         if (bt.Has(BlNameAkr))
         {
            throw new Autodesk.AutoCAD.Runtime.Exception(
                           Autodesk.AutoCAD.Runtime.ErrorStatus.DuplicateBlockName, 
                           $"Блок с именем {BlNameAkr} уже определен в чертеже");
         }

         BlockTableRecord btrPanel = new BlockTableRecord();
         btrPanel.Name = BlNameAkr;
         IdBtrPanel = bt.Add(btrPanel);
         t.AddNewlyCreatedDBObject(btrPanel, true);    
              
         // Добавление полилинии контура
         Polyline plContour = createContour();         
         btrPanel.AppendEntity(plContour);
         t.AddNewlyCreatedDBObject(plContour, true);

         // Добавление окон
         addWindows(btrPanel, t);

         // заполнение плиткой
         addTiles(btrPanel, t);

         // Образмеривание (на Фасаде)
         DimensionFacade dimFacade = new DimensionFacade(btrPanel, t, this);
         dimFacade.Create();
         // Образмеривание (в Форме)
         DimensionForm dimForm = new DimensionForm(btrPanel, t, this);
         dimForm.Create();         
      }

      private string defineBlockPanelAkrName()
      {
         string blName = Settings.Default.BlockPanelAkrPrefixName + MarkWithoutElectric;

         // щечки
         string cheek = Panel.cheeks?.cheek;
         if (!string.IsNullOrWhiteSpace(cheek))
         {
            string cheekPrefix = cheek.Equals("right", StringComparison.OrdinalIgnoreCase) ? "_тп" : "_тл";
            blName += cheekPrefix;
         }
         blName += WindowsPrefix;

         return blName;
      }

      private Polyline createContour()
      {
         Polyline plContour = new Polyline();
         plContour.LayerId = Service.Env.IdLayerContourPanel;

         plContour.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
         plContour.AddVertexAt(0, new Point2d(0, Panel.gab.height), 0, 0, 0);
         plContour.AddVertexAt(0, new Point2d(Panel.gab.length, Panel.gab.height), 0, 0, 0);
         plContour.AddVertexAt(0, new Point2d(Panel.gab.length, 0), 0, 0, 0);
         plContour.Closed= true;

         return plContour;
      }

      private void addWindows(BlockTableRecord btrPanel, Transaction t)
      {         
         if (Panel.windows?.window != null)
         {
            foreach (var item in Panel.windows.window)
            {
               Polyline plWindow = new Polyline();
               plWindow.LayerId = Service.Env.IdLayerContourPanel;
               Point2d ptMinWindow = new Point2d(item.posi.X, item.posi.Y);
               plWindow.AddVertexAt(0, ptMinWindow, 0, 0, 0);
               plWindow.AddVertexAt(0, new Point2d(ptMinWindow.X, ptMinWindow.Y + item.height), 0, 0, 0);
               Point2d ptMaxWindow = new Point2d(ptMinWindow.X + item.width, ptMinWindow.Y + item.height);
               plWindow.AddVertexAt(0, ptMaxWindow, 0, 0, 0);
               plWindow.AddVertexAt(0, new Point2d(ptMinWindow.X + item.width, ptMinWindow.Y), 0, 0, 0);
               plWindow.Closed = true;
               btrPanel.AppendEntity(plWindow);
               t.AddNewlyCreatedDBObject(plWindow, true);

               Openings.Add(new Extents3d(ptMinWindow.Convert3d(), ptMaxWindow.Convert3d()));

               // Поиск соотв марки окна
               var xCenter = item.posi.X + item.width * 0.5;
               var winMark = WindowsBase.First(w => (w.Key.X - xCenter) < 500);
               if (!string.IsNullOrWhiteSpace(winMark.Value))
               {
                  DBText dbTextWin = new DBText();
                  dbTextWin.Position = new Point3d(item.posi.X, item.posi.Y, 0);
                  dbTextWin.TextString = winMark.Value;
                  btrPanel.AppendEntity(dbTextWin);
                  t.AddNewlyCreatedDBObject(dbTextWin, true);
               }
            }
            // Сортировка окон слева-направо
            Openings.Sort((w1, w2) => w1.MinPoint.X.CompareTo(w2.MinPoint.X));
         }
      }

      private void addTiles(BlockTableRecord btrPanel, Transaction t)
      {
         for (int x = 0; x < Panel.gab.length- Settings.Default.TileLenght*0.5; x+=Settings.Default.TileLenght+ Settings.Default.TileSeam)
         {
            for (int y = 0; y < Panel.gab.height- Settings.Default.TileHeight*0.5; y+=Settings.Default.TileHeight+Settings.Default.TileSeam)
            {
               Point3d pt = new Point3d(x, y, 0);

               if (!openingsContainPoint(pt))
               {
                  BlockReference blRefTile = new BlockReference(pt, Service.Env.IdBtrTile);
                  blRefTile.Layer = "0";
                  blRefTile.ColorIndex = 256; // ByLayer

                  btrPanel.AppendEntity(blRefTile);
                  t.AddNewlyCreatedDBObject(blRefTile, true);
               }
            }
         }
      }

      private bool openingsContainPoint(Point3d pt)
      {
         return Openings.Any(b => b.IsPointInBounds(pt));
      }
   }
}
