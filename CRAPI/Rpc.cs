using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CRAPI
{
  static class Rpc
  {

    static int id = 100;

    public static JObject ReadResponseObject(NetworkStream stream)
    {
      try
      {
        List<byte> bytes = new List<byte>();
        while (true)
        {
          byte b = (byte)stream.ReadByte();
          if (b == 0) break;
          bytes.Add(b);
        }
        string resp = Encoding.UTF8.GetString(bytes.ToArray());
        Console.WriteLine("FROM CORE : {0}", resp);
        
        JObject obj = JsonConvert.DeserializeObject(resp) as JObject;
        return obj;
      }
      catch (ArgumentException ex)
      {
        Console.WriteLine("  parse error : {0}\r\n", ex.Message);
        return null;
      }
      catch (Exception)
      {
        return null;
      }
    }

    public static bool ReadResponse(NetworkStream stream)
    {
      try
      {
        List<byte> bytes = new List<byte>();
        while (true)
        {
          byte b = (byte)stream.ReadByte();
          if (b == 0) break;
          bytes.Add(b);
        }
        string resp = Encoding.UTF8.GetString(bytes.ToArray());
        Console.WriteLine("FROM CORE : {0}", resp);
        JsonConvert.DeserializeObject(resp);
        return true;
      }
      catch (ArgumentException ex)
      {
        Console.WriteLine("  parse error : {0}\r\n", ex.Message);
        return true;
      }
      catch (Exception)
      {
        return false;
      }
    }
    private static object _writeLocker = new object();

    public static void Send(NetworkStream stream, string method, object data)
    {
      var rpc = new
      {
        jsonrpc = "2.0",
        method = method,
        @params = data,
        id = id++
      };
      string str = JsonConvert.SerializeObject(rpc);
      Console.WriteLine("TO CORE : {0}", str);
      byte[] bs = Encoding.UTF8.GetBytes(str);
      lock (_writeLocker)
      {
        stream.Write(bs, 0, bs.Length);
        stream.WriteByte(0); // null terminate
      }
    }
    public static void Send(string ip, string method, object data)
    {
      if (!string.IsNullOrEmpty(ip))
      {
        TcpClient client = new TcpClient(ip, 1710) { ReceiveTimeout = 1000 };
        var stream = client.GetStream();

        // send logon
        Send(stream, "Logon", new { User = "NewUser", Password = "1000" });

        Send(stream, method, data);
        // read response
        while (ReadResponse(stream)) ;
      }
    }

    public static void Send(NetworkStream stream, IRPCCommand command)
    {
      Send(stream, command.Method, command);
    }
  }
}
