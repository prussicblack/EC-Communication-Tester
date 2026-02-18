using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Util
{
    public sealed class BoolToBrushConverter : IValueConverter
    {
        public IBrush TrueBrush { get; set; }
        public IBrush FalseBrush { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool bb && bb;
            return b ? (TrueBrush ?? Brushes.LimeGreen) : (FalseBrush ?? Brushes.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
