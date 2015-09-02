﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;

namespace Vil.Acad.AR.PanelColorAlbum.Model
{
   // Объект с ошибками
   public class ErrorObject
   {
      private ObjectId _idEnt;
      private string _errorMsg;
      
      /// <summary>
      /// 
      /// </summary>
      /// <param name="errMsg">Сообщение об ошибке. Для показа пользователю</param>
      /// <param name="idEnt">Если не null, то должен быть примитивом чертежа (для показа пользователю)</param>
      public ErrorObject (string errMsg, ObjectId idEnt)
      {
         _idEnt = idEnt;
         _errorMsg = errMsg;
      }
   }
}
