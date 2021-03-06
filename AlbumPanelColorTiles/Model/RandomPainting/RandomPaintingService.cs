﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AlbumPanelColorTiles.Lib;
using AlbumPanelColorTiles.Options;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace AlbumPanelColorTiles.RandomPainting
{
    // Произвольная покраска участа с % распределением цветов
    public class RandomPaintService
    {
        private ColorAreaSpotSize _colorAreaSize;
        private int _countInsertBlocksSpot;
        private Database _db;
        private Document _doc;
        private Editor _ed;

        //private Extents3d _extentsPrompted;
        private ObjectId _idBlRefColorAreaTemplate;

        private ObjectIdCollection _idColCopy;
        private ObjectId _idMS;

        // зона произвольной покраски
        private Random _rnd;

        private List<Spot> _spots;
        private Stopwatch _stopwatch = new Stopwatch();
        private Dictionary<string, RandomPaint> _trackRandoms;
        private int _xsize; // кол столбцов участков покраски в зоне покраски. Ячейка = Spot (пока равна одной плитке, но потом можно будет задать любой размер кратный плитке).
        private int _ysize; // кол рядов участков в зоне покраски
                            // Распределяемые цвета

        public RandomPaintService()
        {
            _doc = Application.DocumentManager.MdiActiveDocument;
            _ed = _doc.Editor;
            _db = _doc.Database;
            _rnd = new Random();
            _colorAreaSize = new ColorAreaSpotSize(Settings.Default.TileLenght + Settings.Default.TileSeam,
                                       Settings.Default.TileHeight + Settings.Default.TileSeam, "RandomPaint");
        }

        public ColorAreaSpotSize ColorAreaSpotSize { get { return _colorAreaSize; } }
        public Editor Ed { get { return _ed; } }

        public static void CheckBlockColorAre(Database db)
        {
            using (var t = db.TransactionManager.StartTransaction())
            {
                var bt = db.BlockTableId.GetObject(OpenMode.ForRead) as BlockTable;

                if (!bt.Has(Settings.Default.BlockColorAreaName))
                {
                    // Скопировать из шаблона
                    BlockInsert.CopyBlockFromTemplate(Settings.Default.BlockColorAreaName, db);
                }
                t.Commit();
            }
        }

        public static void SetDynParamColorAreaBlock(BlockReference blRefcolorAreaSpot, ColorAreaSpotSize colorAreaSize)
        {
            foreach (DynamicBlockReferenceProperty item in blRefcolorAreaSpot.DynamicBlockReferencePropertyCollection)
            {
                if (string.Equals(item.PropertyName, Settings.Default.BlockColorAreaDynPropLength, StringComparison.InvariantCultureIgnoreCase))
                    item.Value = (double)colorAreaSize.LenghtSpot;
                else if (string.Equals(item.PropertyName, Settings.Default.BlockColorAreaDynPropHeight, StringComparison.InvariantCultureIgnoreCase))
                    item.Value = (double)colorAreaSize.HeightSpot;
            }
        }

        public void PromptExtents()
        {
            _colorAreaSize.ExtentsColorArea = UserPrompt.PromptExtents(_ed, "Укажите первую точку зоны произвольной покраски", "Укажите вторую точку зоны произвольной покраски");
            _spots = new List<Spot>();
        }

        public void Start()
        {
            resetData();
            // Проверка наличия блока зоны покраски
            CheckBlockColorAre(_db);
            // Запрос области для покраски
            PromptExtents();
            // Список всех цветов (по списку листов) - которые можно добавить в распределение покраски зоны
            Dictionary<string, RandomPaint> allProperPaint = getAllProperPaints();
            // Форма для распределения цветов
            FormRandomPainting formProper = new FormRandomPainting(allProperPaint, this, _trackRandoms);
            formProper.Fire += Fire;
            Application.ShowModalDialog(formProper);
            _trackRandoms = formProper.TrackPropers;
        }

        private static bool checkNeighborSomeColor(Spot neighbor, Spot spot)
        {
            if (neighbor != null)
            {
                // если сосед того же цвето, то поиск нового места проживания)
                if (neighbor.Proper.IdLayer == spot.Proper.IdLayer)
                {
                    return false;
                }
            }
            return true;
        }

        private void deleteSpots(List<Spot> spots)
        {
            using (DocumentLock ld = _doc.LockDocument())
            {
                using (var t = _db.TransactionManager.StartTransaction())
                {
                    foreach (var spot in spots)
                    {
                        if (spot != null)
                        {
                            if (!spot.IdBlRef.IsNull && spot.IdBlRef.IsValid && !spot.IdBlRef.IsErased)
                            {
                                var blRef = t.GetObject(spot.IdBlRef, OpenMode.ForWrite, false, true) as BlockReference;
                                blRef.Erase(true);
                            }
                        }
                    }
                    t.Commit();
                }
            }
        }

        private void distributeListAllRandom(List<Spot> spots, int Count, ref Spot[] mixSpots)
        {
            Spot temp;
            foreach (var spot in spots)
            {
                do
                {
                    int number = _rnd.Next(Count - 1);
                    temp = mixSpots[number];
                    if (temp == null)
                    {
                        mixSpots[number] = spot;
                        spot.Index = number;
                    }
                } while (temp != null);
            }
        }

        //private List<Spot> mixingListUpstairsNeighbor(List<Spot> spotsReal, int totalCount)
        //{
        //   Spot[] mixSpots = new Spot[totalCount];
        //   Spot temp;
        //   Spot upstairsNeighbor;
        //   //Spot downstairsNeighbor;
        //   bool mayNext = false;
        //   foreach (var spot in spotsReal)
        //   {
        //      do
        //      {
        //         int number = _rnd.Next(totalCount - 1);
        //         temp = mixSpots[number];
        //         if (temp == null)
        //         {
        //            mayNext = true;
        //            // проверка соседей
        //            if (number < totalCount - 1)
        //            {
        //               upstairsNeighbor = mixSpots[number + 1];
        //               if (upstairsNeighbor != null)
        //               {
        //                  mayNext = false;
        //               }
        //            }
        //            //if (number > 0)
        //            //{
        //            //   downstairsNeighbor = mixSpots[number - 1];
        //            //   if (downstairsNeighbor != null)
        //            //   {
        //            //      mayNext = false;
        //            //   }
        //            //}
        //            if (mayNext)
        //            {
        //               mixSpots[number] = spot;
        //            }
        //         }
        //         else
        //         {
        //            mayNext = false;
        //         }
        //      } while (!mayNext);
        //   }
        //   return mixSpots.ToList();
        //}
        private void distributeListNear100(List<Spot> spots, int Count, ref Spot[] mixSpots)
        {
            Spot temp;
            foreach (var spot in spots)
            {
                int number = _rnd.Next(Count - 1);
                do
                {
                    temp = mixSpots[number];
                    if (temp == null)
                    {
                        mixSpots[number] = spot;
                        spot.Index = number;
                    }
                    else
                    {
                        number++;
                        if (number >= Count)
                        {
                            number = _rnd.Next(Count - 1);
                        }
                    }
                } while (temp != null);
            }
        }

        private void distributeListWithoutNeighborSomeColor(List<Spot> spots, int Count, ref Spot[] mixSpots)
        {
            Spot temp;
            bool mayNext = false;
            foreach (var spot in spots)
            {
                do
                {
                    int number = _rnd.Next(Count - 1); // случайное место для размещения зоны покраски
                    temp = mixSpots[number]; // получение спота в этом расположении
                    if (temp == null) // если там никого нет
                    {
                        mayNext = true;
                        // проверка соседа сверху
                        if (number < (Count - 1))
                        {
                            mayNext = checkNeighborSomeColor(mixSpots[number + 1], spot);
                            if (!mayNext) continue;
                        }
                        // проверка соседа снизу
                        if (number > 0)
                        {
                            mayNext = checkNeighborSomeColor(mixSpots[number - 1], spot);
                            if (!mayNext) continue;
                        }
                        // Проверка соседа справа
                        int indexRightHandMan = number - _ysize;
                        if (indexRightHandMan >= 0)
                        {
                            mayNext = checkNeighborSomeColor(mixSpots[indexRightHandMan], spot);
                            if (!mayNext) continue;
                        }
                        int indexLeftHandMan = number + _ysize;
                        if (indexLeftHandMan <= (Count - 1))
                        {
                            mayNext = checkNeighborSomeColor(mixSpots[indexLeftHandMan], spot);
                            if (!mayNext) continue;
                        }

                        if (mayNext)
                        {
                            mixSpots[number] = spot;
                            spot.Index = number;
                        }
                    }
                    else
                    {
                        mayNext = false;
                    }
                } while (!mayNext);
            }
        }

        /// <summary>
        /// Определение индексов зон покраски
        /// </summary>
        /// <param name="_spots"></param>
        /// <param name="totalTileCount"></param>
        /// <returns>Список зон покраски с записанными индексами</returns>
        private List<Spot> distributeSpots(List<Spot> _spots, int totalTileCount)
        {
            Spot[] distributeSpots = new Spot[totalTileCount];
            var spotOrdered = _spots.GroupBy(s => s.Proper);
            int countPercent = 0;
            foreach (var spots in spotOrdered)
            {
                countPercent += spots.Key.Percent;
                if (countPercent < 70)
                {
                    if (spots.Key.Percent < 25)
                    {
                        // Без соседей одного цвета
                        distributeListWithoutNeighborSomeColor(spots.ToList(), totalTileCount, ref distributeSpots);
                    }
                    else
                    {
                        distributeListAllRandom(spots.ToList(), totalTileCount, ref distributeSpots);
                    }
                }
                else
                {
                    distributeListNear100(spots.ToList(), totalTileCount, ref distributeSpots);
                }
            }
            return distributeSpots.Where(s => s != null).ToList();
        }

        // Огонь
        private void Fire(object sender, EventArgs e)
        {
            try
            {
                // Удаление предыдущей покраски
                if (_spots.Count > 0)
                {
                    deleteSpots(_spots);
                }
                _spots = new List<Spot>();

                List<RandomPaint> propers = ((Dictionary<string, RandomPaint>)sender).Values.ToList();
                _xsize = _colorAreaSize.LenghtSize; //Convert.ToInt32((_extentsPrompted.MaxPoint.X - _extentsPrompted.MinPoint.X) / 300);
                _ysize = _colorAreaSize.HeightSize; //Convert.ToInt32((_extentsPrompted.MaxPoint.Y - _extentsPrompted.MinPoint.Y) / 100);
                int totalTileCount = _xsize * _ysize;
                _ed.WriteMessage("\n");
                _ed.WriteMessage("\n-----------------------------------");
                _ed.WriteMessage($"\nОбщее кол-во зон покраски = {totalTileCount} штук.");
                //Logger.Log.Info("totalTileCount = {0}, xsize={1}, ysize={2}", totalTileCount, _xsize, _ysize);
                int distributedCount = 0;                
                foreach (var proper in propers.OrderByDescending(o=>o.TailCount))
                {
                    proper.TailCount = Convert.ToInt32(proper.Percent * totalTileCount / 100d);
                    distributedCount += proper.TailCount;                    
                    _ed.WriteMessage($"\nРаспределяемый цвет '{proper.LayerName}' - {proper.Percent}% = {proper.TailCount} штук.");
                    //Logger.Log.Info("Распределяемый цвет {0}, процентов {1}, штук {2}", proper.LayerName, proper.Percent, proper.TailCount);
                }

                if (distributedCount > totalTileCount)
                {
                    RandomPaint lastProper = propers.Last();
                    lastProper.TailCount -= distributedCount - totalTileCount;
                    _ed.WriteMessage($"\nУменьшено кол распределяемого цвета '{lastProper.LayerName}' на {(distributedCount - totalTileCount)} штук.");
                    //Logger.Log.Info("Уменьшено кол распр цвета {0} на штук {1}", lastProper.LayerName, (distributedCount - totalTileCount));
                }
                _ed.WriteMessage($"\nВсего рспределено = {distributedCount} ({distributedCount *100 / totalTileCount}%)");
                //Logger.Log.Info("distributedCount = {0}", distributedCount);

                // Сортировка по процентам (начиная с меньшего)
                var propersOrdered = propers.OrderBy(p => p.Percent);
                // Получение общего списка распределения покроаски
                foreach (var proper in propersOrdered)
                {
                    _spots.AddRange(Spot.GetSpots(proper));
                }

                // Распределение зон покраски
                List<Spot> distributedSpots = distributeSpots(_spots, totalTileCount);                

                // Вставка блоков зон
                placementSpots(distributedSpots);

                // отчет
                Report(distributedSpots);

                _ed.Regen();
            }
            catch (System.Exception ex)
            {
                _ed.WriteMessage("\n{0}", ex.ToString());
                Logger.Log.Error(ex, "FormProper_Fire()");
            }
        }

        private void Report(List<Spot> distributedSpots)
        {
            _ed.WriteMessage("\n");
            _ed.WriteMessage("\n---------------------------------------------");
            _ed.WriteMessage($"\nИтого вставленно блоков зон покраски:");
            int count = 0;
            var distributedColors = distributedSpots.GroupBy(g => g.Proper.LayerName).
                Select(s => new { layer = s.Key, percent = s.First().Proper.Percent, count = s.Count() }).
                OrderByDescending(o=>o.count);
            foreach (var item in distributedColors)
            {
                _ed.WriteMessage($"\nСлой '{item.layer}' - {item.percent}% = {item.count}");
                count += item.count;
            }
            _ed.WriteMessage($"\nВсего вставлено блоков покраски: {count} штук.\n");
            _ed.WriteMessage("\n---------------------------------------------");
        }

        private Dictionary<string, RandomPaint> getAllProperPaints()
        {
            Dictionary<string, RandomPaint> propers = new Dictionary<string, RandomPaint>();
            int numProper = 0;
            using (var t = _db.TransactionManager.StartTransaction())
            {
                var lt = _db.LayerTableId.GetObject(OpenMode.ForRead) as LayerTable;
                foreach (ObjectId idLayer in lt)
                {
                    var layer = idLayer.GetObject(OpenMode.ForRead) as LayerTableRecord;
                    RandomPaint proper = new RandomPaint(layer.Name, numProper++, layer.Color.ColorValue, layer.Id);
                    propers.Add(proper.LayerName, proper);
                }
            }
            return propers;
        }

        // Вставка ячейки покраски (пока = одной плитке)
        private void insertSpot(Spot spot, int x, int y, Transaction t)
        {
            Point3d position;
            if (_colorAreaSize.PatternChess)
            {
                double offset = 0;
                if (y % 2 == 0)
                {
                    offset = _colorAreaSize.LenghtSpot * 0.5;
                }
                position = new Point3d(_colorAreaSize.ExtentsColorArea.MinPoint.X + x * _colorAreaSize.LenghtSpot + offset,
                                       _colorAreaSize.ExtentsColorArea.MinPoint.Y + y * _colorAreaSize.HeightSpot, 0);
            }
            else
            {
                position = new Point3d(_colorAreaSize.ExtentsColorArea.MinPoint.X + x * _colorAreaSize.LenghtSpot,
                                       _colorAreaSize.ExtentsColorArea.MinPoint.Y + y * _colorAreaSize.HeightSpot, 0);
            }

            using (IdMapping map = new IdMapping())
            {
                _db.DeepCloneObjects(_idColCopy, _idMS, map, false);
                ObjectId idBlRefCopy = map[_idBlRefColorAreaTemplate].Value;

                if (idBlRefCopy.IsValid && !idBlRefCopy.IsNull)
                {
                    using (var blRefSpot = t.GetObject(idBlRefCopy, OpenMode.ForWrite, false, true) as BlockReference)
                    {
                        blRefSpot.Position = position;
                        blRefSpot.LayerId = spot.Proper.IdLayer;
                        spot.IdBlRef = blRefSpot.Id;
                        _countInsertBlocksSpot++;
                    }
                }
            }
        }

        private void placementSpots(List<Spot> spots)
        {
            using (var lockdoc = _doc.LockDocument())
            {
                using (var t = _db.TransactionManager.StartTransaction())
                {
                    var bt = t.GetObject(_db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var cs = t.GetObject(_db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                    _idMS = cs.Id;
                    var btrColorArea = t.GetObject(bt[Settings.Default.BlockColorAreaName], OpenMode.ForRead) as BlockTableRecord;
                    var blRefColorAreaTemplate = new BlockReference(Point3d.Origin, btrColorArea.Id);
                    cs.AppendEntity(blRefColorAreaTemplate);
                    t.AddNewlyCreatedDBObject(blRefColorAreaTemplate, true);
                    _idBlRefColorAreaTemplate = blRefColorAreaTemplate.Id;
                    SetDynParamColorAreaBlock(blRefColorAreaTemplate, _colorAreaSize);
                    using (_idColCopy = new ObjectIdCollection())
                    {
                        _idColCopy.Add(_idBlRefColorAreaTemplate);
                        ProgressMeter progressMeter = new ProgressMeter();
                        progressMeter.SetLimit(spots.Count);
                        progressMeter.Start("Вставка блоков зон покраски...");

                        _countInsertBlocksSpot = 0;
                        foreach (var spot in spots)
                        {
                            progressMeter.MeterProgress();
                            if (HostApplicationServices.Current.UserBreak())
                                break;
                            if (spot != null)
                            {
                                insertSpot(spot, spot.Index / _ysize, spot.Index % _ysize, t);
                            }
                        }
                        progressMeter.Stop();
                    }
                    Logger.Log.Debug("Вставлено блоков {0}", _countInsertBlocksSpot);

                    blRefColorAreaTemplate.Erase(true);
                    t.Commit();
                }
            }
        }

        private void resetData()
        {
            _spots = new List<Spot>();
        }

        //private void setDynParamColorAreaBlock(BlockReference blRefcolorAreaSpot)
        //{
        //   foreach (DynamicBlockReferenceProperty item in blRefcolorAreaSpot.DynamicBlockReferencePropertyCollection)
        //   {
        //      if (string.Equals(item.PropertyName, "Длина", StringComparison.InvariantCultureIgnoreCase))
        //         item.Value = 300d;
        //      else if (string.Equals(item.PropertyName, "Высота", StringComparison.InvariantCultureIgnoreCase))
        //         item.Value = 100d;
        //   }
        //}
    }
}