﻿using System;
using AcadLib.Layers;
using AlbumPanelColorTiles.Options;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using RTreeLib;

namespace AlbumPanelColorTiles.Panels
{
    // Плитка
    public class Tile : IEquatable<Tile>
    {
        private Extents3d _bounds;
        private Point3d _centerTile;
        private ObjectId _idBlRef;

        // Id внутри определения блока панели (марки СБ).
        public Tile(BlockReference blRefTile)
        {
            _idBlRef = blRefTile.ObjectId;
            _bounds = blRefTile.GeometricExtents;
            _centerTile = new Point3d((_bounds.MaxPoint.X + _bounds.MinPoint.X) * 0.5,
                                      (_bounds.MaxPoint.Y + _bounds.MinPoint.Y) * 0.5, 0);
        }

        public Point3d CenterTile { get { return _centerTile; } }

        public static void GetColorNameFromLayer (string layerName, out string article, out string ncs)
        {
            var splitUnders = layerName.Split(new char[] { '_' }, 2);

            article = splitUnders[0];

            if (splitUnders.Length == 2)
            {
                ncs = splitUnders[1];
            }
            else
            {
                ncs = string.Empty;
            }
        }

        /// <summary>
        /// Покраска блоков плитки в Модели (без блоков АКР-Панелей)
        /// </summary>
        public static void PaintTileInModel(RTree<ColorArea> rtreeColorAreas)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (var t = db.TransactionManager.StartTransaction())
            {
                PaintTileInBtr(SymbolUtilityServices.GetBlockModelSpaceId(db), rtreeColorAreas, Matrix3d.Identity);
                t.Commit();
            }
        }

        public static void PaintTileInBtr(ObjectId idBtr, RTree<ColorArea> rtreeColorAreas, Matrix3d transToModel)
        {
            var btr = idBtr.GetObject(OpenMode.ForRead) as BlockTableRecord;
            foreach (ObjectId idEnt in btr)
            {
                if (idEnt.ObjectClass.Name == "AcDbBlockReference")
                {
                    var blRef = idEnt.GetObject(OpenMode.ForRead, false, true) as BlockReference;
                    string blName = blRef.GetEffectiveName();
                    if (blName.StartsWith(Settings.Default.BlockTileName, StringComparison.OrdinalIgnoreCase))
                    {
                        Tile tile = new Tile(blRef);
                        //Определение покраски плитки
                        Paint paint = ColorArea.GetPaint(tile.CenterTile.TransformBy(transToModel), rtreeColorAreas);
                        if (paint != null)
                        {
                            blRef.UpgradeOpen();
                            blRef.Layer = paint.Layer;
                        }
                    }
                    else if (!MarkSb.IsBlockNamePanel(blName))
                    {
                        // Покраска во вложенных блоках, кроме АРК панелей
                        PaintTileInBtr(blRef.BlockTableRecord, rtreeColorAreas, blRef.BlockTransform * transToModel);
                    }
                }
            }
        }

        public bool Equals(Tile other)
        {
            return _idBlRef.Equals(other._idBlRef) &&
               _bounds.IsEqualTo(other._bounds, Album.Tolerance);
        }

        /// <summary>
        /// Перенос плитки на слой "АР_Плитка"
        /// </summary>
        /// <param name="idBtrMarkSb"></param>
        public static void TilesToLayer(ObjectId idBtrMarkSb)
        {
            // Перенос блоков плиток на слой                 
            var layerTile = LayerExt.CheckLayerState(new LayerInfo("АР_Плитка"));
            var btrMarkSb = idBtrMarkSb.GetObject(OpenMode.ForRead) as BlockTableRecord;
            foreach (ObjectId idEnt in btrMarkSb)
            {
                var blRef = idEnt.GetObject(OpenMode.ForRead, false, true) as BlockReference;
                if (blRef == null) continue;
                if (string.Equals(blRef.GetEffectiveName(), Settings.Default.BlockTileName, StringComparison.CurrentCultureIgnoreCase))
                {
                    blRef.UpgradeOpen();
                    blRef.LayerId = layerTile;
                    blRef.DowngradeOpen();
                }
            }
        }
    }
}