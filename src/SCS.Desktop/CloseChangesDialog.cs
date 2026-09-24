using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SCS.Desktop;

internal enum CloseChoice { Cancel, Save, Discard }
internal sealed class CloseChangesDialog : Window
{
    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;
    public CloseChangesDialog()
    {
        Title="Unsaved changes"; Width=570; SizeToContent=SizeToContent.Height; ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; ShowInTaskbar=false;
        Background=(Brush)Application.Current.FindResource("Bg");
        Foreground=(Brush)Application.Current.FindResource("Ink");
        FontFamily=new FontFamily("Segoe UI");FontSize=14;
        var panel=new StackPanel();
        panel.Children.Add(new TextBlock { Text="You have unsaved changes.",FontSize=18,Margin=new Thickness(0,0,0,12) });
        panel.Children.Add(new TextBlock { Text="Save all stores pending profiles and local preferences. Hotkey installation remains a separate action.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,22) });
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };
        foreach(var (text,choice) in new[]{("Save all and close",CloseChoice.Save),("Discard changes",CloseChoice.Discard),("Cancel",CloseChoice.Cancel)})
        {
            var button=new Button { Content=text, IsCancel=choice==CloseChoice.Cancel };
            button.Click+=(_,_)=>{Choice=choice;DialogResult=choice!=CloseChoice.Cancel;};buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);Content=new Border { Background=Background, Padding=new Thickness(24), Child=panel };
    }
}
