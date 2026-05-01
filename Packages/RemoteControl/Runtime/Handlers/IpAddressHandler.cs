using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Lilium.RemoteControl.Core;

namespace Lilium.RemoteControl.RestApi.Controllers
{
    public class IpAddressHandler : IRequestHandler
    {
        public void Cleanup()
        {
        }

        public bool CanHandle(HttpListenerRequest request)
        {
            return request.Url.AbsolutePath.Equals("/api/ip", StringComparison.OrdinalIgnoreCase) &&
                   request.HttpMethod == "GET";
        }
        
        public async Task HandleRequest(HttpListenerContext context)
        {
            var ipAddresses = GetIPv4Addresses();
            
            var response = new
            {
                success = true,
                addresses = ipAddresses,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            };
            
            var json = JsonUtility.ToJson(response);
            var buffer = Encoding.UTF8.GetBytes(json);
            
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.StatusCode = 200;
            
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
        }
        
        private List<object> GetIPv4Addresses()
        {
            var ipAddresses = new List<object>();
            
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                
                foreach (var networkInterface in interfaces)
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                        continue;
                    
                    var properties = networkInterface.GetIPProperties();
                    foreach (var address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(address.Address))
                        {
                            ipAddresses.Add(new
                            {
                                interfaceName = networkInterface.Name,
                                address = address.Address.ToString()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteControl] Error getting IP addresses: {ex.Message}");
            }
            
            // Fallback method if NetworkInterface fails
            if (ipAddresses.Count == 0)
            {
                try
                {
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in host.AddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ipAddresses.Add(new
                            {
                                interfaceName = "default",
                                address = ip.ToString()
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RemoteControl] Fallback IP detection failed: {ex.Message}");
                }
            }
            
            return ipAddresses;
        }
    }
}