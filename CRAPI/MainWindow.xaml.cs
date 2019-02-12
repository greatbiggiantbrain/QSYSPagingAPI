using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Speech.Synthesis;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

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
      //return;
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
              Console.WriteLine(obj.ToString());
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

      synth = new SpeechSynthesizer();

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
        _player.Stop();
        _player = null;
      };

      _player = new DispatcherTimer();
      _player.Interval = TimeSpan.FromSeconds(60);
      _player.Tick += _player_Tick;
    }

    private void _player_Tick(object sender, EventArgs e)
    {
      string file = string.Format("test{0}.wav", Rpc.id);
      var localPath = @"D:\paging\" + file;
      var remotePath = "Messages/" + file;

      using (SpeechSynthesizer ss = new SpeechSynthesizer())
      {
        ss.SetOutputToWaveFile(localPath);
        ss.Speak(string.Format("Now playing message {0}", Rpc.id));
      }
      using (var webclient = new PreAuthWebClient())
      {
        webclient.AllowStreamBuffering = true;
        webclient.Credentials = new NetworkCredential("Guest", "");  // this could be locked down via Administrator
        webclient.UploadFile(@"http://pratt110.local/cgi-bin/media_put?file=" + remotePath, "PUT", localPath);
      }

      // submit a test page
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
        Message = file
      });
    }

    SpeechSynthesizer synth;
    object _itemsLock = new object();
    DispatcherTimer _player;


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
      Rpc.Send(_tcp_stream, new ControlSet { Name = "DanteInCoreSlotCChannel1SubscriptionChannel", Value = 04 });
    }

    private void btnUnMute_Click(object sender, RoutedEventArgs e)
    {
      Rpc.Send(_tcp_stream, new ControlSet { Name = "SystemMuteMute", Value = -1 });
    }

    private void btnUpload_Click(object sender, RoutedEventArgs e)
    {
      using (var webclient = new PreAuthWebClient())
      {
        webclient.AllowStreamBuffering = true;
        webclient.Credentials = new NetworkCredential("Guest", "");

        var localPath = @"D:\guitar\DiscoJazz.mp3";
        var remotePath = "Audio/DiscoJazz.mp3";
        webclient.UploadFile(@"http://pratt110.local/cgi-bin/media_put?file=" + remotePath, "PUT", localPath);
      }



    }

    private void btnPlay_Click(object sender, RoutedEventArgs e)
    {
      _player.Start();
      _player_Tick(this, null);

    }

    private void btnChangeGroup_Click(object sender, RoutedEventArgs e)
    {
      // FROM THIS DOCUMENT DESCRIBING THE JSON RPC FOR Q-SYS CONTROL
      // http://q-syshelp.qschome.com/#External_Control/Q-SYS_Remote_Control/QRCDocumentation.pdf%3FTocPath%3DExternal%2520Control%7CQ-SYS%2520Remote%2520Control%7C_____1

      // anything you can drag in as a named control can be monitored (and controlled)
      // you could grab the message textbox from the PA router, and the active indicator etc

      // the two items here are "Named Controls" in Q-SYS, if you add them to the Change Group, they will send back their changes
      // you'll have to add the named controls to your design, or change them here to match your design
      Rpc.Send(_tcp_stream, new ChangeGroupAddControl { Id = "MyChangeGroup", Controls = new string[] { "Mic/ControlPageStation-2Gain", "Mic/ControlPageStation-2Mute" } });

      // If you set up auto polling, the change group will respond every x seconds
      // only with the changes since the last response
      Rpc.Send(_tcp_stream, new ChangeGroupAutoPoll { Id = "MyChangeGroup", Rate = 3 });

      // You can also not do an auto poll and just ask on your own using the "ChangeGroup.Poll" method (not implemented here). It would be a one shot thing on your own timer

      /*
A sample return from a ChangeGroup poll
FROM CORE : {"jsonrpc":"2.0","method":"ChangeGroup.Poll","params":{"Id":"MyChangeGroup","Changes":[{"Name":"Mic/ControlPageStation-2Gain","String":"-42.1dB","Value":-42.09999847,"Position":0.48250001,"Indeterminate":true}]}}
{
  "jsonrpc": "2.0",
  "method": "ChangeGroup.Poll",
  "params": {
    "Id": "MyChangeGroup",
    "Changes": [
      {
        "Name": "Mic/ControlPageStation-2Gain",
        "String": "-42.1dB",
        "Value": -42.09999847,
        "Position": 0.48250001,
        "Indeterminate": true
      }
    ]
  }
}
       */

    }
  }

  // ####################
  // INTERFACES
  // ####################

  public interface IRPCCommand
  {
    string Method { get; }
  }

  public interface IRPCResponse
  {

  }

  // ####################
  // RPC COMMANDS
  // ####################

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
    public string String { get; set; }
    public double Position { get; set; }

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

  class ChangeGroupAddControl : IRPCCommand
  {
    public string Method
    {
      get
      {
        return "ChangeGroup.AddControl";
      }
    }

    // the name of this change group
    public string Id { get; set; }
    public string[] Controls { get; set; }
  }

  class ChangeGroupAutoPoll : IRPCCommand
  {
    public string Method
    {
      get
      {
        return "ChangeGroup.AutoPoll";
      }
    }

    // the name of this change group
    public string Id { get; set; }
    public int Rate { get; set; }
  }

  // ####################
  // RPC RESPONSES
  // ####################

  //  public class ChangeGroupResponse : IRPCResponse
  //{

  //  public 
  //}

  /// <summary>
  /// Receive Command for zone status changes
  /// </summary>
  public class ZoneStatus : IRPCResponse
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

  
  // preauth web client is needed for 
  // 7.0+ firmware
  class PreAuthWebClient : WebClient
  {
    public bool AllowStreamBuffering { get; set; }
    public bool Expect100Continue { get; set; }
    protected override WebRequest GetWebRequest(Uri address)
    {
      var rq = base.GetWebRequest(address);
      if (rq is HttpWebRequest)
      {
        HttpWebRequest hrq = (HttpWebRequest)rq;
        hrq.ServicePoint.Expect100Continue = Expect100Continue;
        hrq.PreAuthenticate = true;
        hrq.AllowWriteStreamBuffering = AllowStreamBuffering;
      }
      return rq;
    }
    public PreAuthWebClient()
    {
      Expect100Continue = false;
    }
  }
}
