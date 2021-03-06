﻿using System;
using System.Collections.Generic;
using System.Linq;
using AlbumPanelColorTiles.Options;
using AlbumPanelColorTiles.PanelLibrary;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadLib.RTree.SpatialIndex;
using AcadLib;

namespace AlbumPanelColorTiles.ExportFacade
{
    /// <summary>
    /// Экспортная панель
    /// </summary>
    public class PanelBtrExport
    {
        private Extents3d _extentsByTile;

        public string BlName { get; private set; }
        public ObjectId CaptionLayerId { get; private set; }
        public string CaptionMarkSb { get; private set; }
        public string CaptionPaint { get; private set; }
        public ConvertPanelService CPS { get; private set; }

        // Объекты линий, полилиний в блоке панели (на одном уровне вложенности в блок)
        public List<EntityInfo> EntInfos { get; set; }

        public string ErrMsg { get; private set; }

        public Extents3d ExtentsByTile { get { return _extentsByTile; } }

        public Extents3d ExtentsNoEnd { get; set; }

        public double HeightByTile { get; private set; }

        /// <summary>
        /// Определение блока панели в файле АКР
        /// </summary>
        public ObjectId IdBtrAkr { get; set; }

        /// <summary>
        /// Определение блока панели в экспортированном файле
        /// </summary>
        public ObjectId IdBtrExport { get; set; }

        public ObjectId IdCaptionMarkSb { get; set; }
        public ObjectId IdCaptionPaint { get; set; }
        public List<ObjectId> IdsEndsBottomEntity { get; set; }

        // Объекты торцов панели
        public List<ObjectId> IdsEndsLeftEntity { get; set; }

        public List<ObjectId> IdsEndsRightEntity { get; set; }
        public List<ObjectId> IdsEndsTopEntity { get; set; }
        public List<PanelBlRefExport> Panels { get; private set; }
        public List<Tuple<ObjectId,Extents3d>> Tiles { get; private set; }

        public PanelBtrExport(ObjectId idBtrAkr, ConvertPanelService cps)
        {
            CPS = cps;
            IdBtrAkr = idBtrAkr;
            Panels = new List<PanelBlRefExport>();
            IdsEndsLeftEntity = new List<ObjectId>();
            IdsEndsRightEntity = new List<ObjectId>();
            IdsEndsTopEntity = new List<ObjectId>();
            IdsEndsBottomEntity = new List<ObjectId>();
            EntInfos = new List<EntityInfo>();
            Tiles = new List<Tuple<ObjectId,Extents3d>>();
        }

        public void ConvertBtr()
        {
            using (var btr = IdBtrExport.GetObject(OpenMode.ForWrite) as BlockTableRecord)
            {
                // Итерация по объектам в блоке и выполнение различных операций к элементам
                iterateEntInBlock(btr);

                // Контур панели (так же определяется граница панели без торцов)
                var contourPanel = new ContourPanel(this);
                contourPanel.CreateContour2(btr);

                // Определение торцевых объектов (плитки и полилинии контура торца)
                defineEnds(contourPanel);

                // Удаление объектов торцов из блока панели, если это ОЛ
                if (CaptionMarkSb.StartsWith("ОЛ", StringComparison.CurrentCultureIgnoreCase))
                {
                    deleteEnds(IdsEndsTopEntity);
                    IdsEndsTopEntity = new List<ObjectId>();
                }

                // Повортот подписи марки (Марки СБ и Марки Покраски) и добавление фоновой штриховки
                var caption = new ConvertCaption(this);
                caption.Convert(btr);

                //Если есть ошибки при конвертации, то подпись заголовка этих ошибок
                if (!string.IsNullOrEmpty(ErrMsg))
                {
                    ErrMsg = string.Format("Ошибки в блоке панели {0}: {1}", BlName, ErrMsg);
                }
            }
        }

        public void Def()
        {
            using (var btr = IdBtrAkr.Open(OpenMode.ForRead) as BlockTableRecord)
            {
                BlName = btr.Name;
            }
        }

        public void DeleteEnd(bool isLeftSide)
        {
            if (isLeftSide)
            {
                deleteEnds(IdsEndsLeftEntity);
                IdsEndsLeftEntity = new List<ObjectId>();
            }
            else
            {
                deleteEnds(IdsEndsRightEntity);
                IdsEndsRightEntity = new List<ObjectId>();
            }
        }

        // Удаление мусора из блока
        private static bool deleteWaste(Entity ent)
        {
            if (!(ent is BlockReference) || 
                (string.Equals(ent.Layer, Settings.Default.LayerDimensionFacade, StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(ent.Layer, Settings.Default.LayerDimensionForm, StringComparison.CurrentCultureIgnoreCase)))
            {
                ent.UpgradeOpen();
                ent.Erase();
                return true;
            }
            return false;
        }

        private void defineEnds(ContourPanel contourPanel)
        {
            // условие наличия торцов
            if (ExtentsByTile.Diagonal() < 1000 || ExtentsNoEnd.Diagonal() < 1000 ||
               ExtentsByTile.Diagonal() - ExtentsNoEnd.Diagonal() < 100)
            {
                return;
            }

            // Определение торцевых объектов в блоке
            // Торец слева
            if ((ExtentsNoEnd.MinPoint.X - ExtentsByTile.MinPoint.X) > 400)
            {
                // поиск объектов с координатой близкой к ExtentsByTile.MinPoint.X     
                var endTiles = GetEndTiles(new Extents3d(ExtentsByTile.MinPoint, 
                            new Point3d(ExtentsByTile.MinPoint.X + 300, ExtentsByTile.MaxPoint.Y, ExtentsByTile.MaxPoint.Z)),
                            contourPanel.TreeTiles);
                //var idsEndEntsTemp = getEndEntsInCoord(ExtentsByTile.MinPoint.X, true);
                if (endTiles.Count > 0)
                {
                    HashSet<ObjectId> idsEndLeftEntsHash = new HashSet<ObjectId>();
                    endTiles.ForEach(t => idsEndLeftEntsHash.Add(t));
                    IdsEndsLeftEntity = idsEndLeftEntsHash.ToList();
                }
            }
            // Торец справа
            if ((ExtentsByTile.MaxPoint.X - ExtentsNoEnd.MaxPoint.X) > 400)
            {
                var endTiles = GetEndTiles(new Extents3d(
                    new Point3d(ExtentsByTile.MaxPoint.X - 300, ExtentsByTile.MinPoint.Y, ExtentsByTile.MinPoint.Z),
                    ExtentsByTile.MaxPoint), contourPanel.TreeTiles);
                //var idsEndEntsTemp = getEndEntsInCoord(ExtentsByTile.MaxPoint.X, true);
                if (endTiles.Count > 0)
                {
                    var idsEndRightEntsHash = new HashSet<ObjectId>();
                    endTiles.ForEach(t => idsEndRightEntsHash.Add(t));
                    IdsEndsRightEntity = idsEndRightEntsHash.ToList();
                }
            }
            // Торец сверху
            if ((ExtentsByTile.MaxPoint.Y - ExtentsNoEnd.MaxPoint.Y) > 400)
            {
                var endTiles = GetEndTiles(new Extents3d(
                    new Point3d(ExtentsByTile.MinPoint.X, ExtentsByTile.MaxPoint.Y-300, ExtentsByTile.MaxPoint.Z),
                    ExtentsByTile.MaxPoint), contourPanel.TreeTiles);
                //var idsEndEntsTemp = getEndEntsInCoord(ExtentsByTile.MaxPoint.Y, false);
                if (endTiles.Count > 0)
                {
                    var idsEndTopEntsHash = new HashSet<ObjectId>();
                    endTiles.ForEach(t => idsEndTopEntsHash.Add(t));
                    IdsEndsTopEntity = idsEndTopEntsHash.ToList();
                }
            }
            // Торец снизу
            if ((ExtentsNoEnd.MinPoint.Y - ExtentsByTile.MinPoint.Y) > 400)
            {
                var endTiles = GetEndTiles(new Extents3d(ExtentsByTile.MinPoint,
                    new Point3d(ExtentsByTile.MaxPoint.X, ExtentsByTile.MinPoint.Y + 300, ExtentsByTile.MinPoint.Z)), 
                    contourPanel.TreeTiles);
                //var idsEndEntsTemp = getEndEntsInCoord(ExtentsByTile.MinPoint.Y, false);
                if (endTiles.Count > 0)
                {
                    HashSet<ObjectId> idsEndBotEntsHash = new HashSet<ObjectId>();
                    endTiles.ForEach(t => idsEndBotEntsHash.Add(t));
                    IdsEndsBottomEntity = idsEndBotEntsHash.ToList();
                }
            }
        }

        private List<ObjectId> GetEndTiles(Extents3d extEndTiles, RTree<Tuple<ObjectId,Extents3d>> treeTiles)
        {
            var rect = new Rectangle(extEndTiles);
            var tiles = treeTiles.Intersects(rect);            
            return tiles.Select(s => s.Item1).ToList();
        }

        private void deleteEnds(List<ObjectId> idsList)
        {
            idsList.ForEach(idEnt =>
            {
                using (var ent = idEnt.GetObject(OpenMode.ForWrite, false, true) as Entity)
                {
                    ent.Erase();
                }
            });
        }

        private List<ObjectId> getEndEntsInCoord(double coord, bool isX)
        {
            //coord - координата края торца панели по плитке
            List<ObjectId> resVal = new List<ObjectId>();
            // выбор объектов блока на нужной координате (+- толщина торца = ширине одной плитки - 300)
            foreach (var entInfo in EntInfos)
            {
                if (Math.Abs((isX ? entInfo.Extents.MinPoint.X : entInfo.Extents.MinPoint.Y) - coord) < 330 &&
                    Math.Abs((isX ? entInfo.Extents.MaxPoint.X : entInfo.Extents.MaxPoint.Y) - coord) < 330)
                {
                    resVal.Add(entInfo.Id);
                }
            }
            return resVal;
        }

        public void iterateEntInBlock(BlockTableRecord btr, bool _deleteWaste = true)
        {
            var tilesDict = new Dictionary<Extents3d, Tuple<ObjectId, Extents3d>>();
            _extentsByTile = new Extents3d();
            foreach (ObjectId idEnt in btr)
            {
                if (!idEnt.IsValidEx()) continue;
                using (var ent = idEnt.GetObject(OpenMode.ForRead) as Entity)
                {
                    EntInfos.Add(new EntityInfo(ent));

                    // Если это подпись Марки (на слое Марок)
                    if (ent is DBText && string.Equals(ent.Layer, Settings.Default.LayerMarks, StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Как определить - это текст Марки или Покраски - сейчас Покраска в скобках (). Но вдруг будет без скобок.
                        var textCaption = (DBText)ent;
                        if (textCaption.TextString.StartsWith("("))
                        {
                            CaptionPaint = textCaption.TextString;
                            IdCaptionPaint = idEnt;
                        }
                        else
                        {
                            CaptionMarkSb = textCaption.TextString;
                            IdCaptionMarkSb = idEnt;
                            CaptionLayerId = textCaption.LayerId;
                        }
                        continue;
                    }                   
                    // Если блок - плитка или окно
                    else if (ent is BlockReference)
                    {                        
                        var blRef = ent as BlockReference;
                        var blName = blRef.GetEffectiveName();
                        if (blName.Equals(Settings.Default.BlockTileName, StringComparison.CurrentCultureIgnoreCase))
                        {
                            var ext = blRef.GeometricExtentsСlean();
                            _extentsByTile.AddExtents(ext);

                            try
                            {
                                tilesDict.Add(ext, new Tuple<ObjectId, Extents3d>(blRef.Id, ext));
                            }
                            catch (ArgumentException)
                            {
                                // Ошибка - плитка с такими границами уже есть
                                ErrMsg += "Наложение плиток. ";
                            }
                            catch (Exception ex)
                            {
                                Logger.Log.Error(ex, "iterateEntInBlock - tilesDict.Add(ent.GeometricExtents, ent.GeometricExtents);");
                            }
                            continue;
                        }
                        else if (blName.StartsWith(Settings.Default.BlockWindowName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Окно оставляем
                            continue;
                        }
                    }

                    //// Удаление лишних объектов (мусора)
                    //if (_deleteWaste && deleteWaste(ent)) continue; // Если объект удален, то переход к новому объекту в блоке                    

                    if (_deleteWaste &&
                        (string.Equals(ent.Layer, Settings.Default.LayerDimensionFacade, StringComparison.CurrentCultureIgnoreCase) ||
                        string.Equals(ent.Layer, Settings.Default.LayerDimensionForm, StringComparison.CurrentCultureIgnoreCase)))
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                    }                    
                }
            }

            Tiles = tilesDict.Values.ToList();
            // Проверка
            if (string.IsNullOrEmpty(CaptionMarkSb))
            {
                ErrMsg += "Не наден текст подписи марки панели. ";
            }
            if (string.IsNullOrEmpty(CaptionPaint))
            {
                ErrMsg += "Не наден текст подписи марки покраски панели. ";
            }
            if (ExtentsByTile.Diagonal() < 100)
            {
                ErrMsg += string.Format("Не определены габариты панели по плиткам - диагональ панели = {0}", ExtentsByTile.Diagonal());
            }

            // Определение высоты панели
            HeightByTile = ExtentsByTile.MaxPoint.Y - ExtentsByTile.MinPoint.Y;
        }
    }
}