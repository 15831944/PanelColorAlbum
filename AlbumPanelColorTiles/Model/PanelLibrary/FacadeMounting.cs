﻿using System.Collections.Generic;
using System.Linq;
using AcadLib.Comparers;
using AcadLib.Errors;
using AlbumPanelColorTiles.Options;
using AlbumPanelColorTiles.Panels;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace AlbumPanelColorTiles.PanelLibrary
{
   // Фасад - это ряд блоков монтажных планов этажей с блоками обозначения стороны плана фасада - составляющие один фасада дома
   public class FacadeMounting
   {
      // Этажи фасада (блоки АКР-Панелей и соотв блок Монтажки)
      private List<Floor> _floors;

      private double _xmax;

      // коорднината X для данного фасада
      private double _xmin;

      public FacadeMounting(double x)
      {
         _xmin = x;
         _floors = new List<Floor>();
      }

      public List<Floor> Floors { get { return _floors; } }
      public double XMax { get { return _xmax; } }
      public double XMin { get { return _xmin; } }

      // Создание фасадов по монтажным планам
      public static void CreateFacades(List<FacadeMounting> facades)
      {
         if (facades.Count == 0) return;
         Database db = HostApplicationServices.WorkingDatabase;
         checkLayers(db);
         using (var t = db.TransactionManager.StartTransaction())
         {
            var ms = t.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite) as BlockTableRecord;
            double yFirstFloor = getFirstFloorY(facades); // Y для первых этажей всех фасадов

            using (ProgressMeter progress = new ProgressMeter())
            {
               progress.SetLimit(facades.SelectMany(f => f.Floors).Count());
               progress.Start("Создание фасадов");

               foreach (var facade in facades)
               {
                  double yFloor = yFirstFloor;
                  foreach (var floor in facade.Floors)
                  {
                     yFloor = floor.Storey.Y + yFirstFloor;
                     // Подпись номера этажа
                     captionFloor(facade.XMin, yFloor, floor, ms, t);
                     foreach (var panelSb in floor.PanelsSbInFront)
                     {
                        if (panelSb.PanelAkrLib != null)
                        {
                           Point3d ptPanelAkr = new Point3d(panelSb.GetPtInModel(panelSb.PanelAkrLib).X, yFloor, 0);
                           //testGeom(panelSb, facade, floor, yFloor, t, ms);
                           var blRefPanelAkr = new BlockReference(ptPanelAkr, panelSb.PanelAkrLib.IdBtrPanelAkrInFacade);
                           blRefPanelAkr.Layer = floor.Storey.Layer;
                           ms.AppendEntity(blRefPanelAkr);
                           t.AddNewlyCreatedDBObject(blRefPanelAkr, true);
                           blRefPanelAkr.Draw();
                        }
                     }
                     progress.MeterProgress();
                  }
               }
               t.Commit();
               progress.Stop();
            }
         }
      }

      public static void DeleteOldAkrPanels(List<FacadeMounting> facades)
      {
         // удаление старых АКР-Панелей фасадов
         Database db = HostApplicationServices.WorkingDatabase;
         // список всех акр панелей в модели
         List<ObjectId> idsBlRefPanelAkr = Panel.GetPanelsBlRefInModel(db);

         ProgressMeter progressMeter = new ProgressMeter();
         progressMeter.SetLimit(idsBlRefPanelAkr.Count);
         progressMeter.Start("Удаление старых фасадов");

         foreach (var idBlRefPanelAkr in idsBlRefPanelAkr)
         {
            using (var blRefPanelAkr = idBlRefPanelAkr.Open(OpenMode.ForRead, false, true) as BlockReference)
            {
               var extentsAkr = blRefPanelAkr.GeometricExtents;
               var ptCenterPanelAkr = extentsAkr.Center();
               // если панель входит в границы любого фасада, то удаляем ее
               FacadeMounting facade = facades.Find(f => f.XMin < ptCenterPanelAkr.X && f.XMax > ptCenterPanelAkr.X);
               if (facade != null)
               {
                  blRefPanelAkr.UpgradeOpen();
                  blRefPanelAkr.Erase();
               }
               progressMeter.MeterProgress();
            }
         }
         progressMeter.Stop();
      }

      /// <summary>
      /// Получение фасадов из блоков монтажных планов и обозначений стороны фасада в чертеже
      /// </summary>
      /// <returns></returns>
      public static List<FacadeMounting> GetFacadesFromMountingPlans(PanelLibraryLoadService libLoadServ)
      {
         List<FacadeMounting> facades = new List<FacadeMounting>();

         // Поиск всех блоков монтажных планов в Модели чертежа с соотв обозначением стороны фасада
         List<Floor> floors = Floor.GetMountingBlocks(libLoadServ);

         // Упорядочивание блоков этажей в фасады (блоки монтажек по вертикали образуют фасад)
         // сортировка блоков монтажек по X, потом по Y (все монтажки в одну вертикаль снизу вверх)
         var comparer = new DoubleEqualityComparer(100); // FacadeVerticalDeviation
         foreach (var floor in floors)
         {
            FacadeMounting facade = facades.Find(f => comparer.Equals(f.XMin, floor.XMin));
            if (facade == null)
            {
               // Новый фасад
               facade = new FacadeMounting(floor.XMin);
               facades.Add(facade);
            }
            facade._floors.Add(floor);
         }
         // определение уровней этажей Storey
         defineFloorStoreys(facades);
         return facades;
      }

      public void DefYForUpperAndParapetStorey()
      {
         // определение уровней для Ч и П этажей в этом фасаде
         // уровеь последнего этажа в фасаде
         var floorsNumberType = _floors.Where(f => f.Storey.Type == EnumStorey.Number);
         double yLastNumberFloor = 0;
         if (floorsNumberType.Count() > 0)
         {
            yLastNumberFloor = floorsNumberType.Max(f => f.Storey.Y);
         }
         // чердак
         double yParapet = 0;
         var floorUpper = _floors.Where(f => f.Storey.Type == EnumStorey.Upper).FirstOrDefault();
         if (floorUpper != null)
         {
            var maxHeightPanel = floorUpper.PanelsSbInFront.Where(p => p.PanelAkrLib != null)?.Max(p => p.PanelAkrLib?.HeightPanelByTile);
            if (maxHeightPanel.HasValue)
            {
               floorUpper.Storey.Y = yLastNumberFloor + Settings.Default.FacadeFloorHeight;
               yParapet = floorUpper.Storey.Y + maxHeightPanel.Value;
               floorUpper.Height = maxHeightPanel.Value;
            }
         }
         var floorParapet = _floors.Where(f => f.Storey.Type == EnumStorey.Parapet).FirstOrDefault();
         if (floorParapet != null)
         {
            yParapet = yParapet != 0 ? yParapet : yLastNumberFloor + Settings.Default.FacadeFloorHeight;
            floorParapet.Storey.Y = yParapet;
            var maxHeightPanel = floorParapet.PanelsSbInFront.Where(p => p.PanelAkrLib != null)?.Max(p => p.PanelAkrLib?.HeightPanelByTile);
            if (maxHeightPanel.HasValue)
            {
               floorParapet.Height = maxHeightPanel.Value;
            }
         }
      }

      // Подпись номера этажа
      private static void captionFloor(double x, double yFloor, Floor floor, BlockTableRecord ms, Transaction t)
      {
         DBText textFloor = new DBText();
         textFloor.SetDatabaseDefaults(ms.Database);
         textFloor.Annotative = AnnotativeStates.False;
         textFloor.Height = Settings.Default.FacadeCaptionFloorTextHeight;// 250;// FacadeCaptionFloorTextHeight
         textFloor.TextString = floor.Storey.ToString();
         textFloor.Position = new Point3d(x - Settings.Default.FacadeCaptionFloorIndent, yFloor + (floor.Height * 0.5), 0);
         ms.AppendEntity(textFloor);
         t.AddNewlyCreatedDBObject(textFloor, true);
      }

      private static void checkLayers(Database db)
      {
         // проверкаслоев - если рабочие слои - заблокированны, то разблокировать
         List<string> layersCheck = new List<string>();
         layersCheck.Add(SymbolUtilityServices.LayerZeroName);
         layersCheck.Add(Settings.Default.LayerParapetPanels);
         layersCheck.Add(Settings.Default.LayerUpperStoreyPanels);
         AcadLib.Layers.LayerExt.CheckLayerState(layersCheck.ToArray());
      }

      private static void defineFloorStoreys(List<FacadeMounting> facades)
      {
         // определение уровней этажей Storey
         // этажи с одинаковыми номерами, должны быть на одном уровне во всех фасадах.
         // этажи Ч и П - должны быть последними в этажах одного фасада
         // Определение Storey в фасадах
         List<Storey> storeysAllFacades = new List<Storey>(); // общий список этажей
         facades.ForEach(f =>
         {
            f.DefineFloorStoreys(storeysAllFacades);
            f._xmax = f.Floors.Max(l => l.XMax);
         });
         // назначение Y для нумеррованных этажей
         storeysAllFacades.Sort();
         double y = 0;
         storeysAllFacades.Where(s => s.Type == EnumStorey.Number).ToList().ForEach(s =>
         {
            s.Y = y;
            y += Settings.Default.FacadeFloorHeight;
         });
         // определение отметки для этажей Ч и П - по самой высокой панели АКР в этаже ??? определится при построении
      }

      // определение уровня по Y для первого этажа всех фасадов - отступить 10000 вверх от самого верхнего блока панели СБ.
      private static double getFirstFloorY(List<FacadeMounting> facades)
      {
         double maxYblRefPanelInModel = facades.SelectMany(f => f.Floors).SelectMany(f => f.AllPanelsSbInFloor).Max(p => p.PtCenterPanelSbInModel.Y);
         return maxYblRefPanelInModel + Settings.Default.FacadeIndentFromMountingPlanes;// 10000; // FacadeIndentFromMountingPlanes
      }

      private void checkStoreysFacade()
      {
         // проверка этажей в фасаде.
         // не должно быть одинаковых номеров этажей
         var storeysFacade = _floors.Select(f => f.Storey);
         var storeyNumbersType = storeysFacade.Where(s => s.Type == EnumStorey.Number).ToList();
         var dublicateNumbers = storeyNumbersType.GroupBy(s => s.Number).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
         if (dublicateNumbers.Count > 0)
         {
            string nums = string.Join(",", dublicateNumbers);
            Inspector.AddError(string.Format(
               "Повторяющиеся номера этажей в фасаде. Координата фасада X = {0}. Повторяющиеся номера этажей определенные по блокам монтажных планов этого фасада {1}",
               _xmin, nums));
         }
         // Ч и П могут быть только по одной штуке
         var storeyUpperType = storeysFacade.Where(s => s.Type == EnumStorey.Upper);
         if (storeyUpperType.Count() > 1)
         {
            Inspector.AddError(string.Format(
               "Не должно быть больше одного этажа Чердака в одном фасаде. Для фасада найдено {0} блоков монтажных планов определенных как чердак. Координата фасада X = {1}.",
               storeyUpperType.Count(), _xmin));
         }
         var storeyParapetType = storeysFacade.Where(s => s.Type == EnumStorey.Parapet);
         if (storeyParapetType.Count() > 1)
         {
            Inspector.AddError(string.Format(
               "Не должно быть больше одного этажа Парапета в одном фасаде. Для фасада найдено {0} блоков монтажных планов определенных как парапет. Координата фасада X = {1}.",
               storeyParapetType.Count(), _xmin));
         }
      }

      private void DefineFloorStoreys(List<Storey> storeysNumbersTypeInAllFacades)
      {
         // Определение этажей в этажах фасада.
         List<Storey> storeysFacade = new List<Storey>();
         _floors.ForEach(f => f.DefineStorey(storeysNumbersTypeInAllFacades));
         _floors.Sort();
         // проверка этажей в фасаде.
         checkStoreysFacade();
      }

      //private static void testGeom(MountingPanel panelSb, Facade facade, Floor floor, double yFloor, Transaction t, BlockTableRecord ms)
      //{
      //   // Точка центра панели СБ
      //   DBPoint ptPanelSbInModel = new DBPoint(panelSb.PtCenterPanelSbInModel);
      //   ms.AppendEntity(ptPanelSbInModel);
      //   t.AddNewlyCreatedDBObject(ptPanelSbInModel, true);
      //   // Точка вставки панели АКР
      //   DBPoint ptPanelArkInModel = new DBPoint(new Point3d(panelSb.GetPtInModel(panelSb.PanelAkrLib).X, yFloor, 0));
      //   ms.AppendEntity(ptPanelArkInModel);
      //   t.AddNewlyCreatedDBObject(ptPanelArkInModel, true);
      //}
   }
}