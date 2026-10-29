using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
namespace ReunionLocale;
public sealed class VoiceColor : IValueConverter
{
 public object Convert(object value,Type targetType,object parameter,CultureInfo culture) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value?.ToString() switch {"me"=>"#E3ECFF","pc-1"=>"#DFF6EC","pc-2"=>"#FFF0D8","pc-3"=>"#EDE5FF","pc-4"=>"#FFE3E9",_=>"#F0F2F5"}));
 public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}

