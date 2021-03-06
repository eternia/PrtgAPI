﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrtgAPI.Parameters;
using PrtgAPI.Tests.UnitTests.Support;

namespace PrtgAPI.Tests.IntegrationTests.Infrastructure
{
    [TestClass]
    public class PrtgClientConnectionTests : BasePrtgClientTest
    {
        [TestMethod]
        public void Logic_Client_NullCredentials()
        {
            var server = $"http://{Settings.Server}";
            string username = null;
            string password = null;

            AssertEx.Throws<ArgumentNullException>(
                () => new PrtgClient(server, username, password),
                "Value cannot be null.\r\nParameter name: username"
            );
        }

        [TestMethod]
        public void Logic_Client_InvalidCredentials()
        {
            var server = $"http://{Settings.Server}";
            string username = "a";
            string password = "a";

            try
            {
                var client = new PrtgClient(server, username, password);
                AssertEx.Fail("Invalid credentials were specified however an exception was not thrown");
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message != "Could not authenticate to PRTG; the specified username and password were invalid.")
                {
                    AssertEx.Fail(ex.Message);
                }
            }
            catch (Exception ex)
            {
                AssertEx.Fail(ex.Message);
            }
        }

        [TestMethod]
        public void Logic_Client_NullServer()
        {
            string server = null;

            AssertEx.Throws<ArgumentNullException>(
                () => new PrtgClient(server, Settings.UserName, Settings.Password),
                "Value cannot be null.\r\nParameter name: server"
            );
        }

        [TestMethod]
        public void Logic_Client_InvalidServer()
        {
            string server = "a";

            try
            {
                var client = new PrtgClient(server, Settings.UserName, Settings.Password);
            }
            catch (WebException ex)
            {
                if (ex.Message != $"The remote name could not be resolved: '{server}'")
                    AssertEx.Fail($"Request did not fail with expected error message: {ex.Message}");
            }
        }

        [TestMethod]
        public void Logic_Client_InvalidRequest()
        {
            var client = new PrtgClient(Settings.ServerWithProto, Settings.UserName, Settings.Password);
            AssertEx.Throws<PrtgRequestException>(() => client.RemoveObject(0), "Some of the selected objects could not be deleted");
        }

        [TestMethod]
        public async Task Logic_Client_InvalidRequestAsync()
        {
            var client = new PrtgClient(Settings.ServerWithProto, Settings.UserName, Settings.Password);
            await AssertEx.ThrowsAsync<PrtgRequestException>(async () => await client.RemoveObjectAsync(0), "Some of the selected objects could not be deleted");
        }

        [TestMethod]
        public void Logic_Client_ConnectWithHttp()
        {
            var server = $"http://{Settings.Server}";

            var client = new PrtgClient(server, Settings.UserName, Settings.Password);
        }

        [TestMethod]
        public void Logic_Client_ConnectWithHttps()
        {
            var server = $"https://{Settings.Server}";

            try
            {
                var localClient = new PrtgClient(server, Settings.UserName, Settings.Password);
            }
            catch (WebException ex)
            {
                if (ex.Message != "Server rejected HTTPS connection on port 443. Please confirm expected server protocol and port, PRTG Core Service is running and that any SSL certificate is trusted")
                {
                    throw;
                }
            }
        }

        [TestMethod]
        public async Task Logic_Client_ConnectWithHttps_Async()
        {
            var server = $"https://{Settings.Server}";

            var localClient = new PrtgClient(server, Settings.UserName, client.PassHash, AuthMode.PassHash);

            //Get the method
            var engine = localClient.GetInternalProperty("RequestEngine");
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var methods = engine.GetType().GetMethods(flags).Where(m => m.Name == "ExecuteRequestAsync").ToList();
            var method = methods.First(m => m.GetParameters().Any(p => p.ParameterType.Name == "IJsonParameters"));

            //Construct the parameters
            var parameters = PrtgAPIHelpers.GetPassHashParameters(Settings.Password);

            try
            {
                await (Task)method.Invoke(engine, new object[] {parameters, null, CancellationToken.None});
            }
            catch (WebException ex)
            {
                if (ex.Message != "Server rejected HTTPS connection on port 443. Please confirm expected server protocol and port, PRTG Core Service is running and that any SSL certificate is trusted")
                {
                    throw;
                }
            }
        }

        [TestMethod]
        public void Logic_Client_RetryRequest()
        {
            Logic_Client_RetryRequestInternal(localClient => localClient.GetSensors(), false);
        }

        [TestMethod]
        public void Logic_Client_RetryRequest_Async()
        {
            Logic_Client_RetryRequestInternal(localClient =>
            {
                var sensors = localClient.GetSensorsAsync().Result;
            }, true);
        }

        [TestMethod]
        public void Logic_Client_Timeout()
        {
            var localClient = GetTimeoutClient();

            AssertEx.Throws<System.TimeoutException>(() => localClient.GetSensors(), "The server timed out while executing request");
        }

        [TestMethod]
        public async Task Logic_Client_Timeout_Async()
        {
            var localClient = GetTimeoutClient();

            await AssertEx.ThrowsAsync<System.TimeoutException>(async () => await localClient.GetSensorsAsync(), "The server timed out while executing request");
        }

        private PrtgClient GetTimeoutClient()
        {
            var localClient = new PrtgClient(Settings.ServerWithProto, Settings.UserName, Settings.Password);
            var engine = localClient.GetInternalProperty("RequestEngine");
            var webInterface = engine.GetInternalField("webClient");

            var httpClient = new HttpClient
            {
                Timeout = new TimeSpan(0, 0, 0, 0, 1)
            };

            webInterface.GetType().GetField("asyncClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(webInterface, httpClient);            

            return localClient;
        }

        private void Logic_Client_RetryRequestInternal(Action<PrtgClient> action, bool isAsync)
        {
            var initialThread = Thread.CurrentThread.ManagedThreadId;

            Impersonator.ExecuteAction(() =>
            {
                var retriesMade = 0;
                var retriesToMake = 3;

                var coreService = new ServiceController("PRTGCoreService", Settings.Server);

                var localClient = new PrtgClient(Settings.ServerWithProto, Settings.UserName, Settings.Password);
                localClient.RetryRequest += (sender, args) =>
                {
                    Logger.LogTestDetail($"Handling retry {retriesMade + 1}");

                    if (!isAsync)
                        AssertEx.AreEqual(initialThread, Thread.CurrentThread.ManagedThreadId, "Event was not handled on initial thread");
                    retriesMade++;
                };
                localClient.RetryCount = retriesToMake;

                Logger.LogTestDetail("Stopping PRTG Service");

                coreService.Stop();
                coreService.WaitForStatus(ServiceControllerStatus.Stopped);

                try
                {
                    action(localClient);
                }
                catch (AggregateException ex)
                {
                    if (ex.InnerException != null && ex.InnerException.GetType() == typeof (AssertFailedException))
                        throw ex.InnerException;
                }
                catch (WebException)
                {
                }
                finally
                {
                    Logger.LogTestDetail("Starting PRTG Service");
                    coreService.Start();
                    coreService.WaitForStatus(ServiceControllerStatus.Running);

                    Logger.LogTestDetail("Sleeping for 30 seconds");
                    Thread.Sleep(30000);

                    Logger.LogTestDetail("Refreshing and sleeping for 20 seconds");
                    localClient.RefreshObject(Settings.Device);
                    Thread.Sleep(20000);

                    ServerManager.WaitForObjects();
                }

                AssertEx.AreEqual(retriesToMake, retriesMade, "An incorrect number of retries were made.");
            });
        }

        [TestMethod]
        public void Logic_Client_CancelsSynchronous()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            AssertEx.Throws<TaskCanceledException>(
                () => client.GetSensors(new SensorParameters(), cts.Token),
                "A task was canceled."
            );
        }

        [TestMethod]
        public async Task Logic_Client_CancelsAsynchronous()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await AssertEx.ThrowsAsync<TaskCanceledException>(
                async () => await client.GetSensorsAsync(new SensorParameters(), cts.Token),
                "A task was canceled."
            );
        }
    }
}
