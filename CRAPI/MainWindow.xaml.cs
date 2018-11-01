using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Threading;
using System.Windows;
using System.Windows.Data;

namespace CRAPI
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {
    public ObservableCollection<object> Log { get; set; }
    NetworkStream _tcp_stream = null;

    public MainWindow()
    {
      InitializeComponent();
      Log = new ObservableCollection<object>();
      BindingOperations.EnableCollectionSynchronization(Log, _itemsLock);
      Log.CollectionChanged += Log_CollectionChanged;
      this.DataContext = this;

      // just a normal TCP connection
      TcpClient tcp = new TcpClient("pratt110.local", 1710);
      _tcp_stream = tcp.GetStream();

      int pageId = 0;

      // the core needs to be pinged to keep the connection alive
      Timer t = new Timer(new TimerCallback((st) => {
        Rpc.Send(_tcp_stream, new NoOp());
      }), null, 0, 5000);

      System.Threading.Thread rxThread = new System.Threading.Thread(() =>
      {
        // this thread reads responses from the core
        try
        {
          // json is returned
          var obj = Rpc.ReadResponseObject(_tcp_stream);
          while (obj != null)
          {
            try
            {
                JToken oMethod;
                string method = "";
                if (obj.TryGetValue("method", out oMethod))
                {
                  method = oMethod.ToString();
                }
                if (method == "PA.PageStatus")
                {
                  var prms = obj["params"];
                  pageId = (int)prms["PageID"];
                }
                else if (method == "PA.ZoneStatus")
                {
                  ZoneStatus z = new ZoneStatus(obj);
                  this.Dispatcher.BeginInvoke(new Action(() => { lock(_itemsLock) Log.Add(z); }));
                }
            }
            catch (ThreadAbortException TAE)
            {
              Console.WriteLine("Read Thread LOOP Aborted");
              break;
            }
            catch(ThreadInterruptedException TIE)
            {
              Console.WriteLine("Read Thread LOOP Interrupted");
              break;
            }
            catch(Exception EX)
            {
              Console.WriteLine("Read Thread LOOP Exception (Continuing on) {0}", EX.Message);
            }
            obj = Rpc.ReadResponseObject(_tcp_stream);// as IDictionary<string, object>;
          }
        }
        catch (ThreadAbortException TAE)
        {
          Console.WriteLine("Read Thread Aborted");
        }
        catch (ThreadInterruptedException TIE)
        {
          Console.WriteLine("Read Thread Interrupted");
        }
        catch (Exception GENX)
        {
          Console.WriteLine("Read Thread generic exception (bailing out) {0}", GENX.Message);
        }
        Console.WriteLine("LEFT WATCHER THREAD");
      });
      rxThread.Start(); // start the response listener

      // tell the core we want to watch zone changes
      Rpc.Send(_tcp_stream, new WatchEnable { Enabled = true });

      // submite a test page
      Rpc.Send(_tcp_stream, new PageSubmit
      {
        Mode = "message",
        Originator = "Test App",
        Description = "this is a test...",
        Station = 5,
        Zones = new int[] { 1, 2, },
        ZoneTags = new string[] { },
        Priority = 2,
        Start = true,
        Preamble = "",
        Message = "Southwest_as_a_reminder.wav"
      });

      // Close everything when the app closes
      this.Closing += (sender, e) =>
      {
        t.Change(0, Timeout.Infinite);
        if (!rxThread.Join(500))
        {
          rxThread.Abort();
        }
        _tcp_stream.Close();
        tcp.Close();

      };
    }

    object _itemsLock = new object();


    private void Log_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
      // scroll the log Listbox to the bottome when it changes
      if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
      {
        if (logBox.Items.Count > 0)
        {
          logBox.ScrollIntoView(logBox.Items[logBox.Items.Count - 1]);
        }
      }
    }

    private void btnMute_Click(object sender, RoutedEventArgs e)
    {
      Rpc.Send(_tcp_stream, new ControlSet { Name = "SystemMuteMute", Value = 1 });
    }

    private void btnUnMute_Click(object sender, RoutedEventArgs e)
    {
      Rpc.Send(_tcp_stream, new ControlSet { Name = "SystemMuteMute", Value = -1 });
    }
  }

  public interface IRPCCommand
  {
    string Method { get; }
  }

  class ControlSet : IRPCCommand
  {
    public string Method
    {
      get
      {
        return "Control.Set";
      }
    }

    public string Name { get; set; }
    public double Value { get; set; }

  }

  /// <summary>
  /// Send Command to tell core to send status changes back
  /// </summary>
  class WatchEnable : IRPCCommand
  {
    public string Method
    {
      get
      {
        return "PA.ZoneStatusConfigure";
      }
    }

    public bool Enabled { get; set; }
    public override string ToString()
    {
      return base.ToString();
    }
  }

  /// <summary>
  /// Send Command to queue a new page
  /// </summary>
  class PageSubmit : IRPCCommand
  {
    public string Mode { get; set; }
    public string Originator { get; set; }
    public string Description { get; set; }
    public int[] Zones { get; set; }
    public string[] ZoneTags { get; set; }
    public int Priority { get; set; }
    public string Preamble { get; set; }
    public string Message { get; set; }
    public bool Start { get; set; }
    public int Station { get; set; }

    public string Method
    {
      get
      {
        return "PA.PageSubmit";
      }
    }

    public override string ToString()
    {
      return string.Format("Request to play {0}", Message);
    }
  }

  /// <summary>
  /// Receive Command for zone status changes
  /// </summary>
  public class ZoneStatus
  {
    public ZoneStatus(JObject o)
    {
      Zone = o.GetValue("params").Value<int>("Zone");
      Active = o.GetValue("params").Value<bool>("Active");
    }

    public DateTime Time { get; set; }
    public int Zone { get; set; }
    public bool Active { get; set; }

    public override string ToString()
    {
      string isActive = Active ? "" : "Not";
      return string.Format("Zone {0} is {1} Active", Zone, isActive);
    }
  }

  /// <summary>
  /// Send Command to ping the core
  /// </summary>
  class NoOp : IRPCCommand
  {
    public string Method
    {
      get
      {
        return "NoOp";
      }
    }

    public override string ToString()
    {
        return "PING";
      }
    }
}
