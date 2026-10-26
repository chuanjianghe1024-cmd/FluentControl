using FluentControl.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private void BuildBrightnessTransition(StackPanel section)
    {
        var options = state.Settings.Adaptive.Transition;
        options.Normalize();
        section.Children.Add(new TextBlock { Text = T("亮度过渡曲线", "Brightness transition curve"), FontSize = 18 });
        section.Children.Add(new TextBlock { Text = T("横轴：时间进度；纵轴：变化完成比例。拖动控制点编辑曲线。阶梯预览为 30 → 40。", "X: elapsed time; Y: change progress. Drag control points to edit. Staircase preview: 30 → 40."), TextWrapping = TextWrapping.Wrap });
        var canvas = new Canvas { Width = 440, Height = 240, Background = new SolidColorBrush(Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Left };
        var curve = new ComboBox { Header = T("过渡形状", "Transition shape"), ItemsSource = new[] { T("线性", "Linear"), T("S 曲线", "S curve"), T("自定义", "Custom") },
            SelectedIndex = options.Curve == "linear" ? 0 : options.Curve == "custom" ? 2 : 1 };
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap };
        const double left = 36, top = 12, width = 388, height = 200;
        Point Position(double x, double y) => new(left + width*x, top + height*(1-y));
        int dragging = 0;
        void Draw()
        {
            canvas.Children.Clear();
            void Line(double x1,double y1,double x2,double y2,Windows.UI.Color color,double thickness=1)
            {
                var a=Position(x1,y1);var b=Position(x2,y2);
                canvas.Children.Add(new Line { X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y,Stroke=new SolidColorBrush(color),StrokeThickness=thickness });
            }
            for(int i=0;i<=4;i++) { Line(i/4d,0,i/4d,1,Colors.Gray); Line(0,i/4d,1,i/4d,Colors.Gray); }
            void Label(string value,double x,double y)
            { var text=new TextBlock { Text=value,FontSize=11 };Canvas.SetLeft(text,x);Canvas.SetTop(text,y);canvas.Children.Add(text); }
            Label("100%",0,top); Label("0%",8,top+height-12);Label("0%",left,top+height+4);Label("100%",left+width-32,top+height+4);
            for(int i=1;i<=100;i++) Line((i-1)/100d,options.Evaluate((i-1)/100d),i/100d,options.Evaluate(i/100d),Colors.DodgerBlue,2);
            double lastX=0,lastY=0;
            int samples=(int)Math.Ceiling(options.DurationMs/options.TickMs);
            for(int i=1;i<=samples;i++)
            {
                double x=Math.Min(1,i*options.TickMs/options.DurationMs);
                double y=(Math.Round(30+10*options.Evaluate(x))-30)/10;
                Line(lastX,lastY,x,lastY,Colors.Orange,2);Line(x,lastY,x,y,Colors.Orange,2);
                var at=Position(x,y);var point=new Ellipse { Width=5,Height=5,Fill=new SolidColorBrush(Colors.Orange) };
                Canvas.SetLeft(point,at.X-2.5);Canvas.SetTop(point,at.Y-2.5);canvas.Children.Add(point);lastX=x;lastY=y;
            }
            if(options.Curve=="custom")
            {
                Line(0,0,options.X1,options.Y1,Colors.DodgerBlue);Line(1,1,options.X2,options.Y2,Colors.DodgerBlue);
                foreach(var point in new[] { Position(options.X1,options.Y1),Position(options.X2,options.Y2) })
                { var handle=new Ellipse { Width=16,Height=16,Fill=new SolidColorBrush(Colors.DodgerBlue) };Canvas.SetLeft(handle,point.X-8);Canvas.SetTop(handle,point.Y-8);canvas.Children.Add(handle); }
            }
            detail.Text = F("时长 {0} ms · 采样间隔约 {1} ms · 最多 {2} 次（相同整数跳过）", "Duration {0} ms · interval ≈ {1} ms · up to {2} writes (duplicate integers skipped)", options.DurationMs, Math.Round(options.TickMs), samples);
        }
        void Number(string title,int value,int min,int max,Action<int> change)
        {
            var box=new NumberBox { Header=title,Value=value,Minimum=min,Maximum=max,Width=240,HorizontalAlignment=HorizontalAlignment.Left,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact };
            box.ValueChanged+=(_,e)=> { if(!double.IsFinite(e.NewValue)) return;change((int)Math.Round(e.NewValue));options.Normalize();SaveAdaptiveSettings();Draw(); };
            section.Children.Add(box);
        }
        curve.SelectionChanged+=(_,_)=> { options.Curve=curve.SelectedIndex==0?"linear":curve.SelectedIndex==2?"custom":"smooth";SaveAdaptiveSettings();Draw(); };
        section.Children.Add(curve);
        Number(T("总时长（毫秒）", "Duration (milliseconds)"),options.DurationMs,1000,3000,v=>options.DurationMs=v);
        Number(T("最小下发间隔（毫秒）", "Minimum write interval (milliseconds)"),options.IntervalMs,100,500,v=>options.IntervalMs=v);
        Number(T("调整次数（0 为自动）", "Adjustment count (0 = automatic)"),options.Steps,0,30,v=>options.Steps=v);
        canvas.PointerPressed+=(_,e)=>
        {
            if(options.Curve!="custom") return;
            var p=e.GetCurrentPoint(canvas).Position;
            double Distance(Point q)=>(p.X-q.X)*(p.X-q.X)+(p.Y-q.Y)*(p.Y-q.Y);
            var a=Distance(Position(options.X1,options.Y1));var b=Distance(Position(options.X2,options.Y2));
            if(Math.Min(a,b)>400) return;dragging=a<=b?1:2;canvas.CapturePointer(e.Pointer);e.Handled=true;
        };
        canvas.PointerMoved+=(_,e)=>
        {
            if(dragging==0) return;var p=e.GetCurrentPoint(canvas).Position;
            double x=Math.Clamp((p.X-left)/width,0,1),y=Math.Clamp(1-(p.Y-top)/height,0,1);
            if(dragging==1) { options.X1=Math.Min(x,options.X2);options.Y1=Math.Min(y,options.Y2); }
            else { options.X2=Math.Max(x,options.X1);options.Y2=Math.Max(y,options.Y1); }
            Draw();e.Handled=true;
        };
        void Finish() { if(dragging==0)return;dragging=0;SaveAdaptiveSettings(); }
        canvas.PointerReleased+=(_,e)=> { Finish();canvas.ReleasePointerCapture(e.Pointer); };
        canvas.PointerCaptureLost+=(_,_)=>Finish();
        section.Children.Add(canvas);section.Children.Add(detail);Draw();
    }

    private void BuildNodeSignalPicker(StackPanel section, LightSourceSubscription source)
    {
        if(string.IsNullOrEmpty(source.DeviceId))return;
        var load=new Button { Content=T("读取传感器清单", "Load sensor inventory") };
        var picker=new ComboBox { Header=T("亮度使用的信号", "Signal for brightness"),MinWidth=240 };
        var inventory=new TextBlock { TextWrapping=TextWrapping.Wrap };
        section.Children.Add(load);section.Children.Add(picker);section.Children.Add(inventory);
        load.Click+=async(_,_)=>
        {
            load.IsEnabled=false;
            try
            {
                var frame=await FcsGatewayClient.NodeAsync(source.DeviceId,"sensors.list");
                if(closed)return;
                var choices=new List<(string Sensor,string Signal,string Label)>();
                var labels=new List<string>();
                foreach(var sensor in frame.GetProperty("sensors").EnumerateArray())
                    foreach(var signal in sensor.GetProperty("signals").EnumerateArray())
                    {
                        string sid=sensor.GetProperty("id").GetString()!,id=signal.GetProperty("id").GetString()!;
                        var label=sid+" / "+id+" ("+signal.GetProperty("unit").GetString()+")";
                        labels.Add(label+" · "+sensor.GetProperty("driver").GetProperty("status").GetString());
                        if(id=="illuminance" && signal.GetProperty("unit").GetString()=="lx" && signal.GetProperty("readable").GetBoolean())
                            choices.Add((sid,id,label));
                    }
                inventory.Text=string.Join("\n",labels);picker.Items.Clear();
                foreach(var item in choices)picker.Items.Add(new ComboBoxItem { Content=item.Label,Tag=(item.Sensor,item.Signal) });
                picker.SelectedIndex=choices.FindIndex(item=>item.Sensor==source.SensorId && item.Signal==source.SignalId);
            }
            catch(Exception) { inventory.Text=T("无法读取传感器清单", "Sensor inventory unavailable"); }
            finally { load.IsEnabled=true; }
        };
        picker.SelectionChanged+=(_,_)=> { if(picker.SelectedItem is ComboBoxItem { Tag: ValueTuple<string,string> selected })
            {source.SensorId=selected.Item1;source.SignalId=selected.Item2;SaveAdaptiveSettings();} };
    }
}
