using System.Text;
using System.Windows;

namespace QBLyricEditor;

public partial class App : Application
{
    public App()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
