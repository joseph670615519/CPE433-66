using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

//verified it works

namespace DNWS
{
  class ClientInfoPlugin : IPlugin
  {
    protected static Dictionary<String, int> statDictionary = null;
    public ClientInfoPlugin()
    {
      if (statDictionary == null)
      {
        statDictionary = new Dictionary<String, int>();

      }
    }

    public void PreProcessing(HTTPRequest request)
    {
      if (statDictionary.ContainsKey(request.Url))
      {
        statDictionary[request.Url] = (int)statDictionary[request.Url] + 1;
      }
      else
      {
        statDictionary[request.Url] = 1;
      }
    }

    public HTTPResponse GetResponse(HTTPRequest request)
    {
      HTTPResponse response = null;
      StringBuilder sb = new StringBuilder();

      IPEndPoint endpoint = IPEndPoint.Parse(request.getPropertyByKey("remoteendpoint"));
      sb.Append("<html><body><pre>");
      sb.AppendFormat("Client IP: {0}<br/>\n", endpoint.Address);
      sb.AppendFormat("Client Port: {0}<br/>\n", endpoint.Port);
      sb.AppendFormat("Browser Information: {0}<br/>\n", request.getPropertyByKey("user-agent").Trim());
      sb.AppendFormat("Accept Language: {0}<br/>\n", request.getPropertyByKey("accept-language").Trim());
      sb.AppendFormat("Accept Encoding: {0}<br/>\n", request.getPropertyByKey("accept-encoding").Trim());

      sb.Append("</pre></body></html>");

      response = new HTTPResponse(200);
      response.body = Encoding.UTF8.GetBytes(sb.ToString());
      return response;
    }


    public HTTPResponse PostProcessing(HTTPResponse response)
    {
      throw new NotImplementedException();
    }
  }
}