﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlbumPanelColorTiles.Options;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AlbumPanelColorTiles.Model.Base
{
   public class DimensionForm : DimensionAbstract
   {
      public DimensionForm(BlockTableRecord btrPanel, Transaction t, Panel panel) : base(btrPanel, t, panel)
      {

      }

      public void Create()
      {
         double xCenter = panel.gab.length * 0.5;
         Matrix3d matrixMirr = Matrix3d.Mirroring(new Line3d(new Point3d(xCenter, 0, 0), new Point3d(xCenter, 1000, 0)));

         // Создание определения блока образмеривыания - пустого
         btrDim = createBtrDim("ОБРФ_", panel.Service.Env.IdLayerDimForm);
         // Размеры сверху
         sizesTop(true, matrixMirr);
         // Размеры снизу 
         sizesBot();

         // Отзеркалить блок размеров в форме
         using (var blRefDim = this.idBlRefDim.GetObject(OpenMode.ForWrite, false, true) as BlockReference)
         {
            blRefDim.TransformBy(matrixMirr);
         }
      }
   }
}