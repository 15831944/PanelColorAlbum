﻿namespace Vil.Acad.AR.AlbumPanelColorTiles.Model
{
   public class Options
   {
      //
      // Имена блоков
      //
      public string BlockColorAreaName = "АКР_Зона-покраски";
      public string BlockPanelPrefixName = "АКР_Панель_";
      public string BlockStampContent = "АКР_Рамака_Содержание";
      public string BlockStampMarkAR = "АКР_Рамка_МаркаАР";
      public string BlockTileName = "АКР_Плитка";
      // Суффикс для торцевых панелей слева
      public string endLeftPanelSuffix = "_тл";
      // Суффикс для торцевых панелей справа
      public string endRightPanelSuffix = "_тп";

      //
      // Слои
      //
      /// <summary>
      /// Слой для подписей марок панелей
      /// </summary>
      public string LayerMarks = "АР_Марки";
      /// <summary>
      /// Слой для панелей чердака. Панель на этом слое считается панелью чердака.
      /// </summary>
      public string LayerUpperStoreyPanels = "АР_Панели_Чердак";
      // Слой окон (отключать на листе панели для формы)
      public string LayerWindows = "АР_Окна";
      public string LayerDimensionFacade = "АР_Размеры на фасаде";
      public string LayerDimensionForm = "АР_Размеры в форме";

      //
      // Шаблоны
      //
      /// <summary>
      /// Путь к файлу шаблона содержания (АКР_Шаблон_Содержание.dwg)
      /// root - в корне программы.
      /// </summary>
      public string SheetTemplateFileContent = "root";
      /// <summary>
      /// Путь к файлу шаблона МаркиСБ с шаблоном листа для МАрки АР. АКР_Шаблон_МаркаСБ.dwg
      /// root - в корне программы.
      /// </summary>
      public string SheetTemplateFileMarkSB = "root";
      /// <summary>
      /// Имя листа шаблона содержания в файле шаблона листов.
      /// </summary>
      public string SheetTemplateLayoutNameForContent = "Содержание";
      /// <summary>
      /// Имя листа шаблона для Марки АР в файле шаблона листов.
      /// </summary>
      public string SheetTemplateLayoutNameForMarkAR = "TemplateMarkAR";
   }
}