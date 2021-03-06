﻿using System;
using System.Collections.Generic;
using System.Linq;
using AcadLib.Errors;
using AlbumPanelColorTiles.Checks;
using AlbumPanelColorTiles;
using AlbumPanelColorTiles.Base;
using AlbumPanelColorTiles.Panels;
using AlbumPanelColorTiles.Select;
using AlbumPanelColorTiles.Options;
using AlbumPanelColorTiles.Sheets;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using RTreeLib;
using AcadLib;

namespace AlbumPanelColorTiles
{
    // Альбом колористических решений.
    public class Album
    {
        public const string KEYNAMECHECKMARKPAINTING = "CheckMarkPainting";
        public const string KEYNAMENEWMODE = "NEWMODE";
        public const string KEYNAMESORTPANELS = "SortPanels";
        public const string KEYNAMEENDSINPAINTING = "EndsInMarkPainting";
        public const string KEYNAMESPLITINDEXPAINTING = "SplitIndexPainting";        
        public const string KEYNAMENUMBERFIRSTFLOOR = "NumberFirstFloor";
        public const string KEYNAMENUMBERFIRSTSHEET = "NumberFirstSheet";
        public const string KEYNAMENUMBERADDSHEETCONTENT = "NumberAddSheetContent";
        public const string KEYNAMEISTILEARTICLE = "IsTileArticle";
        public const string REGAPPPATH = @"Software\Vildar\AKR";
        public const string REGKEYABBREVIATE = "Abbreviate";

        //private string _abbreviateProject;
        private List<ColorArea> _colorAreas;

        private List<Paint> _colors; // Набор цветов используемых в альбоме.
        private Database _db;

        private Document _doc;

        private List<MarkSb> _marksSB;

        private SheetsSet _sheetsSet;

        // Сокращенное имя проеккта
        //private int _numberFirstFloor;
        //private int _numberFirstSheet;
        private List<Storey> _storeys;        

        public Album()
        {
            _doc = Application.DocumentManager.MdiActiveDocument;
            _db = _doc.Database;
            Date = DateTime.Now;
            StartOptions = new StartOption();
            StartOptions.LoadDefault();
        }

        //public int NumberFirstSheet { get { return _numberFirstSheet; } }
        public static Tolerance Tolerance { get { return Tolerance.Global; } }

        // public string AbbreviateProject { get { return _abbreviateProject; } }
        public string AlbumDir { get; set; }

        public List<Paint> Colors { get { return _colors; } }
        public DateTime Date { get; private set; }
        public Document Doc { get { return _doc; } }
        public string DwgFacade { get { return _doc.Name; } }
        public List<MarkSb> MarksSB { get { return _marksSB; } }
        public List<Panels.Section> Sections { get; set; }
        public SheetsSet SheetsSet { get { return _sheetsSet; } }
        public StartOption StartOptions { get; private set; }
        public AlbumInfo AlbumInfo { get; set; }
        public List<Storey> Storeys { get { return _storeys; } }
        //public BaseService BasePanelsService { get; private set; }
        /// <summary>
        /// Включена/выключена подпись артикла в плитке (в альбома панелей)
        /// </summary>
        //public bool IsTileArticleOn { get; set; }

        /// <summary>
        /// Общий расход плитки на альбом.
        /// Список плиток по цветам
        /// </summary>
        public List<TileCalc> TotalTilesCalc { get; private set; }

        public PanelLibrary.PanelLibraryLoadService LibLoadService { get; set; }

        // Сброс блоков панелей в чертеже. Замена панелей марки АР на панели марки СБ
        public static void ResetBlocks()
        {
            // Для покраски панелей, нужно, чтобы в чертеже были расставлены блоки панелей Марки СБ.
            // Поэтому, при изменении зон покраски, перед повторным запуском команды покраски панелей и создания альбома,
            // нужно восстановить блоки Марки СБ (вместо Марок АР).
            // Блоки панелей Марки АР - удалить.

            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            using (var t = db.TransactionManager.StartTransaction())
            {
                var bt = t.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var ms = t.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                var checkedBlocks = new HashSet<string>();

                foreach (ObjectId idEnt in ms)
                {
                    if (idEnt.IsValidEx() && idEnt.ObjectClass.Name == "AcDbBlockReference")
                    {
                        var blRef = t.GetObject(idEnt, OpenMode.ForRead, false, true) as BlockReference;
                        if (MarkSb.IsBlockNamePanel(blRef.Name))
                        {                            
                            // Если это панель марки АР, то заменяем на панель марки СБ.
                            if (MarkSb.IsBlockNamePanelMarkAr(blRef.Name))
                            {
                                string markSb = MarkSb.GetMarkSbName(blRef.Name);// может быть с суффиксом торца _тп или _тл
                                string markSbBlName = MarkSb.GetMarkSbBlockName(markSb);// может быть с суффиксом торца _тп или _тл
                                if (!bt.Has(markSbBlName))
                                {
                                    // Нет определения блока марки СБ.
                                    // Такое возможно, если после покраски панелей, сделать очистку чертежа (блоки марки СБ удалятся).
                                    MarkSb.CreateBlockMarkSbFromAr(blRef.BlockTableRecord, markSbBlName);
                                    string errMsg = "\nНет определения блока для панели Марки СБ " + markSbBlName +
                                                   ". Оно создано из панели Марки АР " + blRef.Name +
                                                   ". Если внутри блока Марки СБ были зоны покраски, то в блоке Марки АР они были удалены." +
                                                   "Необходимо проверить блоки и заново запустить программу.";
                                    ed.WriteMessage("\n" + errMsg);
                                    // Надо чтобы проектировщик проверил эти блоки, может в них нужно добавить зоны покраски (т.к. в блоках марки АР их нет).
                                }
                                var idBtrMarkSb = bt[markSbBlName];
                                var blRefMarkSb = new BlockReference(blRef.Position, idBtrMarkSb);
                                blRefMarkSb.SetDatabaseDefaults();
                                blRefMarkSb.Layer = blRef.Layer;
                                ms.UpgradeOpen();
                                ms.AppendEntity(blRefMarkSb);
                                t.AddNewlyCreatedDBObject(blRefMarkSb, true);
                                // Перенос плитки на слой "АР_Плитка"
                                if (checkedBlocks.Add(markSbBlName))
                                {
                                    Tile.TilesNormalize(idBtrMarkSb);
                                }
                            }
                            // Перенос плитки на слой "АР_Плитка"
                            if (checkedBlocks.Add(blRef.Name))
                            {
                                Tile.TilesNormalize(blRef.BlockTableRecord);
                            }
                        }
                    }
                }
                Caption captionPanels = new Caption(db);
                // Удаление определений блоков Марок АР.
                foreach (ObjectId idBtr in bt)
                {
                    if (!idBtr.IsValidEx()) continue;
                    var btr = t.GetObject(idBtr, OpenMode.ForRead) as BlockTableRecord;
                    if (MarkSb.IsBlockNamePanel(btr.Name))
                    {
                        // Если это блок панели Марки АР
                        if (MarkSb.IsBlockNamePanelMarkAr(btr.Name))
                        {
                            // Удаление всех вхожденний бллока
                            var idsBlRef = btr.GetBlockReferenceIds(true, true);
                            foreach (ObjectId idBlRef in idsBlRef)
                            {
                                var blRef = t.GetObject(idBlRef, OpenMode.ForWrite, false, true) as BlockReference;
                                blRef.Erase(true);
                            }
                            // Удаление определение блока Марки АР
                            btr.UpgradeOpen();
                            btr.Erase(true);
                        }
                        else
                        {
                            // Подпись марки блока
                            string panelMark = btr.Name.Substring(Settings.Default.BlockPanelAkrPrefixName.Length);
                            captionPanels.AddMarkToPanelBtr(panelMark, idBtr);
                        }
                    }
                }
                t.Commit();
            }
        }

        // Проверка панелей на чертеже и панелей в памяти (this)
        public void CheckPanelsInDrawingAndMemory()
        {
            // Проверка зон покраски
            var colorAreasCheck = ColorArea.GetColorAreas(SymbolUtilityServices.GetBlockModelSpaceId(_db), this);
            // сравнение фоновых зон
            if (!colorAreasCheck.SequenceEqual(_colorAreas))
            {
                throw new System.Exception("Изменились зоны покраски. Рекомендуется выполнить повторную покраску панелей командой PaintPanels.");
            }

            // Проверка панелей
            // Определение покраски панелей.
            var rtreeColorAreas = ColorArea.GetRTree(colorAreasCheck);

            SelectionBlocks selBlocks = new SelectionBlocks(_db);
            selBlocks.SelectBlRefsInModel(StartOptions.SortPanels);

            var marksSbCheck = MarkSb.GetMarksSB(rtreeColorAreas, this, "Проверка панелей...", selBlocks.IdsBlRefPanelAr);
            //RenamePanelsToArchitectIndex(marksSbCheck);
            if (!marksSbCheck.SequenceEqual(_marksSB))
            {
                throw new System.Exception("Панели изменились после последнего выполнения команды покраски. Рекомендуется выполнить повторную покраску панелей командой PaintPanels.");
            }
        }

        public void ChecksBeforeCreateAlbum()
        {
            if (_marksSB == null)
            {
                throw new System.Exception("Не определены панели марок АР.");
            }
            // Проверка есть ли панелеи марки АР
            bool hasMarkAR = false;
            foreach (var markSb in _marksSB)
            {
                if (markSb.MarksAR.Count > 0)
                {
                    hasMarkAR = true;
                    break;
                }
            }
            if (!hasMarkAR)
            {
                throw new System.Exception("Не определены панели марок АР.");
            }
        }

        // Создание альбома панелей
        public void CreateAlbum()
        {
            ChangeJob.ChangeJobService.Init();

            // Пока не нужны XML панели для создания альбома.
            //if (StartOptions.NewMode)
            //{
            //   BasePanelsService = new BaseService();
            //   BasePanelsService.ReadPanelsFromBase();
            //}

            // Определение включен ли артикул подписи в плитке (по состоянию замороженности слоя - Артикул пдлитки)
            //IsTileArticleOn = GetTileArticleState();

            // Подсчет общего кол плитки на альбом
            TotalTilesCalc = TileCalc.CalcAlbum(this);

            // Создание папки альбома панелей
            _sheetsSet = new SheetsSet(this);
            _sheetsSet.CreateAlbum();

            // Заполнение атрибутов марок покраски в блоках монтажек
            try
            {
                var libService = new PanelLibrary.PanelLibraryLoadService();
                libService.FillMarkPainting(this);
                this.LibLoadService = libService;
            }
            catch (System.Exception ex)
            {
                string errMsg = "Ошибка заполнения марок покраски в монтажки - libService.FillMarkPainting(_album);";
                this.Doc.Editor.WriteMessage($"\n{errMsg} - {ex.Message}");
                Logger.Log.Error(ex, errMsg);
            }

            //// Проверка новых панелей, которых нет в библиотеке
            //try
            //{
            //   PanelLibrarySaveService.CheckNewPanels();
            //}
            //catch (Exception ex)
            //{
            //   Logger.Log.Error(ex, "Не удалось проверить есть ли новые панели в чертеже фасада, которых нет в библиотеке.");
            //}

            // Если есть панели с изменениями - создание задания.
            try
            {
                ChangeJob.ChangeJobService.CreateJob(this);
            }
            catch (System.Exception ex)
            {
                Inspector.AddError($"Ошибка при создании Задания на Изменение марок покраски - {ex.Message}");
            }

            // Еспорт списка панелей в ексель.
            try
            {
                ExportToExcel.Export(this);
            }
            catch (System.Exception ex)
            {
                Logger.Log.Error(ex, "Не удалось экспортировать панели в Excel.");
            }

            // вставка итоговой таблицы по плитке
            try
            {
                TotalTileTable tableTileTotal = new TotalTileTable(this);
                tableTileTotal.InsertTableTotalTile();
            }
            catch (System.Exception ex)
            {
                Logger.Log.Error(ex, "Не удалось вставить итоговую таблицу плитки на альбом.");
            }
        }

        /// <summary>
        /// Определение включен ли артикул подписи в плитке (по состоянию замороженности слоя - Артикул пдлитки)
        /// </summary>        
        private bool GetTileArticleState()
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (var lt = db.LayerTableId.Open(OpenMode.ForRead) as LayerTable)
            {
                if (lt.Has(Settings.Default.LayerTileArticle))
                {
                    using (var ltr = lt[Settings.Default.LayerTileArticle].Open(OpenMode.ForRead) as LayerTableRecord)
                    {
                        if (ltr != null)
                        {
                            return !ltr.IsFrozen;
                        }
                    }
                }
                return false;
            }
        }

        // Поиск цвета в списке цветов альбома
        public Paint GetPaint(string layerName)
        {
            Paint paint = _colors.Find(c => c.Layer == layerName);
            if (paint == null)
            {
                // Определение цвета слоя
                Database db = HostApplicationServices.WorkingDatabase;
                Color color = null;
                using (var lt = db.LayerTableId.Open(OpenMode.ForRead) as LayerTable)
                {
                    using (var ltr = lt[layerName].Open(OpenMode.ForRead) as LayerTableRecord)
                    {
                        color = ltr.Color;
                    }
                }
                paint = new Paint(layerName, color);
                _colors.Add(paint);
            }
            return paint;
        }

        // Покраска панелей в модели (по блокам зон покраски)
        public void PaintPanels()
        {
            // Запрос начальных значений - Аббревиатуры, Номера первого этажа, Номера первого листа
            //promptStartOptions();         
            StartOptions = StartOptions.PromptStartOptions();

            // Определение марок покраски панелей (Марок АР).
            // Создание определениц блоков марок АР.
            // Покраска панелей в чертеже.

            // В Модели должны быть расставлены панели Марки СБ и зоны покраски.
            // сброс списка цветов.
            _colors = new List<Paint>();

            // Определение зон покраски в Модели
            _colorAreas = ColorArea.GetColorAreas(SymbolUtilityServices.GetBlockModelSpaceId(_db), this);
            RTree<ColorArea> rtreeColorAreas = ColorArea.GetRTree(_colorAreas);
            // Бонус - покраска блоков плитки разложенных просто в Модели         
            try
            {
                Tile.PaintTileInModel(rtreeColorAreas);
            }
            catch (System.Exception ex)
            {
                Logger.Log.Error(ex, "Tile.PaintTileInModel(rtreeColorAreas);");
            }

            // Сброс блоков панелей Марки АР на панели марки СБ.
            ResetBlocks();

            // Проверка чертежа
            Inspector.Clear();
            CheckDrawing checkDrawing = new CheckDrawing();
            checkDrawing.CheckForPaint();
            if (Inspector.HasErrors)
            {
                throw new System.Exception("\nПокраска панелей не выполнена, в чертеже найдены ошибки в блоках панелей, см. выше.");
            }

            SelectionBlocks selBlocks = new SelectionBlocks(_db);
            selBlocks.SelectBlRefsInModel(StartOptions.SortPanels);
            // В чертеже не должно быть панелей марки АР
            if (selBlocks.IdsBlRefPanelAr.Count > 0)
            {
                Inspector.AddError($"Ошибка. При покраске в чертеже не должно быть блоков панелей марки АР. Найдено {selBlocks.IdsBlRefPanelAr.Count} блоков марки АР.",
                      icon: System.Drawing.SystemIcons.Error);
            }
            Sections = Panels.Section.GetSections(selBlocks.SectionsBlRefs);

            // Определение покраски панелей.
            _marksSB = MarkSb.GetMarksSB(rtreeColorAreas, this, "Покраска панелей...", selBlocks.IdsBlRefPanelSb);
            if (_marksSB?.Count == 0)
            {
                throw new System.Exception("Не найдены блоки панелей в чертеже. Выполните команду AKR-Help для просмотра справки к программе.");
            }

            // Проверить всели плитки покрашены. Если есть непокрашенные плитки, то выдать сообщение об ошибке.
            if (Inspector.HasErrors)
            {
                throw new System.Exception("\nПокраска не выполнена, не все плитки покрашены. См. список непокрашенных плиток в форме ошибок.");
            }

            // Определение принадлежности блоков панелеи секциям
            Panels.Section.DefineSections(this);

            // Переименование марок АР панелей в соответствии с индексами архитекторов (Э2_Яр1)
            RenamePanelsToArchitectIndex(_marksSB);

            // Создание определений блоков панелей покраски МаркиАР
            CreatePanelsMarkAR();

            // Замена вхождений блоков панелей Марки СБ на блоки панелей Марки АР.
            ReplaceBlocksMarkSbOnMarkAr();

            //// Определение принадлежности блоков панелеи фасадам
            //Facade.DefineFacades(this);

            // Добавление подписей к панелям
            Caption caption = new Caption(_marksSB);
            caption.CaptionPanels();
        }

        // Сброс данных расчета панелей
        public void ResetData()
        {
            // Набор цветов используемых в альбоме.
            Inspector.Clear();
            _colors = null;
            _colorAreas = null;
            ObjectId _idLayerMarks = ObjectId.Null;
            _marksSB = null;
            _sheetsSet = null;
        }

        // Создание определений блоков панелей марки АР
        private void CreatePanelsMarkAR()
        {
            ProgressMeter progressMeter = new ProgressMeter();
            progressMeter.Start("Создание определений блоков панелей марки АР ");
            progressMeter.SetLimit(_marksSB.Count);
            progressMeter.Start();

            foreach (var markSB in _marksSB)
            {
                progressMeter.MeterProgress();
                if (HostApplicationServices.Current.UserBreak())
                    throw new System.Exception("Отменено пользователем.");

                foreach (var markAR in markSB.MarksAR)
                {
                    markAR.CreateBlock();
                }
            }
            progressMeter.Stop();
        }

        // Переименование марок АР панелей в соответствии с индексами архитекторов (Э2_Яр1)
        private void RenamePanelsToArchitectIndex(List<MarkSb> marksSB)
        {
            // Определение этажа панели.
            _storeys = Storey.IdentificationStoreys(marksSB, StartOptions.NumberFirstFloor);

            // Определение индексов окон 
            var alphanumComparer = new AcadLib.Comparers.AlphanumComparator();
            var groupWindows = marksSB.GroupBy(m => m.MarkSbClean).Where(g => g.Skip(1).Any());
            foreach (var win in groupWindows)
            {
                var winSorted = win.Where(w => !string.IsNullOrEmpty(w.WindowName)).OrderBy(w => w.WindowName, alphanumComparer);
                int i = 1;
                foreach (var markSbWin in winSorted)
                {
                    markSbWin.WindowIndex = i++;
                }
            }

            // Маркировка Марок АР по архитектурному индексу                  
            foreach (var markSB in marksSB)
            {
                markSB.DefineArchitectMarks();
            }
        }

        // Замена вхождений блоков панелей Марки СБ на панели Марки АР
        private void ReplaceBlocksMarkSbOnMarkAr()
        {
            ProgressMeter progressMeter = new ProgressMeter();
            progressMeter.SetLimit(_marksSB.Count);
            progressMeter.Start("Замена вхождений блоков панелей Марки СБ на панели Марки АР ");

            using (var t = _db.TransactionManager.StartTransaction())
            {
                var ms = t.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite) as BlockTableRecord;
                foreach (var markSb in _marksSB)
                {
                    if (HostApplicationServices.Current.UserBreak())
                        throw new System.Exception("Отменено пользователем.");

                    markSb.ReplaceBlocksSbOnAr(t, ms);
                    progressMeter.MeterProgress();
                }
                t.Commit();
            }
            progressMeter.Stop();
        }
    }
}