﻿using System.Collections.Generic;
using System.Linq;
using AcadLib.Comparers;
using AcadLib.Errors;
using AlbumPanelColorTiles.Panels;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace AlbumPanelColorTiles.ExportFacade
{
    public class ConvertPanelService
    {
        // Слой для контура панелей
        private ObjectId _idLayerContour;

        public ConvertPanelService(ExportFacadeService service)
        {
            Service = service;
        }

        public Database DbExport { get; set; }

        public ObjectId IdLayerContour {
            get {
                if (_idLayerContour.IsNull)
                {
                    _idLayerContour = ContourPanel.CreateLayerContourPanel();
                }
                return _idLayerContour;
            }
        }

        public List<PanelBtrExport> PanelsBtrExport { get; private set; }
        public ExportFacadeService Service { get; private set; }

        /// <summary>
        /// преобразование панеелей
        /// </summary>
        public void ConvertBtr()
        {
            ProgressMeter progress = new ProgressMeter();
            progress.SetLimit(PanelsBtrExport.Count);
            progress.Start("Преобразование блоков панелей в экспортированном файле");
            // Преобразования определений блоков панелей
            foreach (var panelBtr in PanelsBtrExport)
            {
                progress.MeterProgress();
                if (HostApplicationServices.Current.UserBreak())
                    throw new System.Exception("Отменено пользователем.");
                try
                {
                    panelBtr.ConvertBtr();
                    if (!string.IsNullOrEmpty(panelBtr.ErrMsg))
                    {
                        Inspector.AddError(panelBtr.ErrMsg, panelBtr.Panels.First().Extents, panelBtr.Panels.First().IdBlRefAkr,
                           icon: System.Drawing.SystemIcons.Error);
                    }
                }
                catch (System.Exception ex)
                {
                    Inspector.AddError($"Ошибка конвертации блока панели - {ex.Message}", icon: System.Drawing.SystemIcons.Error);
                    //Logger.Log.Error(ex, "Ошибка конвертиации экспортрированного блока панели");
                }
            }
            progress.Stop();
        }

        public void ConvertEnds()
        {
            // Преобразование торцов фасада
            List<ConvertEndsFacade> convertsEnds = new List<ConvertEndsFacade>();
            DoubleEqualityComparer comparer = new DoubleEqualityComparer(500);
            // Все вхождения блоков панелей с торцами слева
            var panelsWithLeftEndsByX = PanelsBtrExport.SelectMany(pBtr => pBtr.Panels).
                        Where(pBlRef => pBlRef.PanelBtrExport.IdsEndsLeftEntity.Count > 0).
                        GroupBy(pBlRef => pBlRef.Position.X, comparer);
            foreach (var itemLefEndsByY in panelsWithLeftEndsByX)
            {
                try
                {
                    ConvertEndsFacade convertEndsFacade = new ConvertEndsFacade(itemLefEndsByY, true, this);
                    convertEndsFacade.Convert();
                    convertsEnds.Add(convertEndsFacade);
                }
                catch (Exception ex)
                {
                    Inspector.AddError($"Ошибка преобразования торцов панелей с координатой X точки вставки = {itemLefEndsByY.Key}. {ex}");
                }
            }

            // Все вхождения блоков панелей с торцами справа
            var panelsWithRightEndsByX = PanelsBtrExport.SelectMany(pBtr => pBtr.Panels).
                        Where(pBlRef => pBlRef.PanelBtrExport.IdsEndsRightEntity.Count > 0).
                        GroupBy(pBlRef => pBlRef.Position.X, comparer);
            foreach (var itemRightEndsByY in panelsWithRightEndsByX)
            {
                try
                {
                    ConvertEndsFacade convertEndsFacade = new ConvertEndsFacade(itemRightEndsByY, false, this);
                    convertEndsFacade.Convert();
                    convertsEnds.Add(convertEndsFacade);
                }
                catch (Exception ex)
                {
                    Inspector.AddError($"Ошибка преобразования торцов панелей с координатой X точки вставки = {itemRightEndsByY.Key}. {ex}");
                }
            }

            // удаление торцов
            convertsEnds.ForEach(c => c.DeleteEnds());
        }

        public void DefinePanels(List<Facade> facades)
        {
            // определение экспортируемых панелей - в файле АКР
            Dictionary<ObjectId, PanelBtrExport> dictPanelsBtrExport = new Dictionary<ObjectId, PanelBtrExport>();

            //RTreeLib.RTree<Facade> treeFacades = new RTreeLib.RTree<Facade>();
            //facades.ForEach(f =>
            //               {
            //                  try
            //                  {
            //                     treeFacades.Add(ColorArea.GetRectangleRTree(f.Extents), f);
            //                  }
            //                  catch { }
            //               });      

            ProgressMeter progress = new ProgressMeter();
            progress.SetLimit(Service.SelectPanels.IdsBlRefPanelAr.Count);
            progress.Start("Определение панелей в файле АКР");

            foreach (var idBlRefPanel in Service.SelectPanels.IdsBlRefPanelAr)
            {
                progress.MeterProgress();
                if (HostApplicationServices.Current.UserBreak())
                    throw new System.Exception("Отменено пользователем.");
                using (var blRef = idBlRefPanel.Open(OpenMode.ForRead, false, true) as BlockReference)
                {
                    // панель определения блока
                    PanelBtrExport panelBtrExport;
                    if (!dictPanelsBtrExport.TryGetValue(blRef.BlockTableRecord, out panelBtrExport))
                    {
                        panelBtrExport = new PanelBtrExport(blRef.BlockTableRecord, this);
                        dictPanelsBtrExport.Add(blRef.BlockTableRecord, panelBtrExport);
                    }
                    panelBtrExport.Def();

                    // панель вхождения блока
                    PanelBlRefExport panelBlRefExport = new PanelBlRefExport(blRef, panelBtrExport);
                    panelBtrExport.Panels.Add(panelBlRefExport);

                    //// определение фасада панели
                    //panelBlRefExport.Facade = defFacadeForPanel(treeFacades, blRef, panelBtrExport, panelBlRefExport);
                }
            }
            PanelsBtrExport = dictPanelsBtrExport.Values.ToList();
            progress.Stop();
        }

        public void Purge()
        {
            // Очистка экспортированного чертежа от блоков образмеривания которые были удалены из панелей после копирования
            ObjectIdGraph graph = new ObjectIdGraph();
            foreach (var panelBtr in PanelsBtrExport)
            {
                ObjectIdGraphNode node = new ObjectIdGraphNode(panelBtr.IdBtrExport);
                graph.AddNode(node);
            }
            DbExport.Purge(graph);
        }

        //private Facade defFacadeForPanel(RTreeLib.RTree<Facade> treeFacades, BlockReference blRef,
        //                                    PanelBtrExport panelBtrExport, PanelBlRefExport panelBlRefExport)
        //{
        //   Facade resVal = null;
        //   RTreeLib.Point pt = new RTreeLib.Point(panelBlRefExport.Position.X, panelBlRefExport.Position.Y, 0);
        //   var facadesFind = treeFacades.Nearest(pt, 100);
        //   if (facadesFind.Count == 1)
        //   {
        //      resVal = facadesFind.First();
        //   }
        //   else if (facadesFind.Count == 0)
        //   {
        //      Inspector.AddError($"Не определен фасад для панели {panelBtrExport.BlName}",
        //             blRef.GeometricExtents, panelBlRefExport.IdBlRefAkr, icon: System.Drawing.SystemIcons.Error);
        //   }
        //   else if (facadesFind.Count > 1)
        //   {
        //      Inspector.AddError($"Найдено больше одного фасада для панели {panelBtrExport.BlName}",
        //             blRef.GeometricExtents, panelBlRefExport.IdBlRefAkr, icon: System.Drawing.SystemIcons.Error);
        //   }
        //   return resVal;
        //}
    }
}