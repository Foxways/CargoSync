using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OrganizationImportTool.Eadaptor
{
    /// <summary>
    /// Submits CargoWise Native XML to an eAdaptor endpoint. Verified contract: HTTP POST the
    /// raw Native document with HTTP Basic auth and Content-Type application/xml; CargoWise
    /// returns a &lt;UniversalResponse&gt;. No SOAP envelope is required.
    /// </summary>
    public class EadaptorClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        private readonly string _url;
        private readonly string _username;
        private readonly string _password;

        public EadaptorClient(string url, string username, string password)
        {
            _url = url;
            _username = username;
            _password = password;
        }

        /// <summary>Max send attempts on transient transport failures (5xx / 408 / 429 / timeouts).</summary>
        public int MaxAttempts { get; set; } = 3;

        public async Task<EadaptorResponse> SendAsync(string nativeXml, CancellationToken ct = default)
        {
            EadaptorResponse last = new EadaptorResponse { TransportOk = false, Error = "No attempt made." };

            for (int attempt = 1; attempt <= Math.Max(1, MaxAttempts); attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var msg = new HttpRequestMessage(HttpMethod.Post, _url)
                    {
                        Content = new StringContent(nativeXml, Encoding.UTF8, "application/xml")
                    };
                    if (!string.IsNullOrEmpty(_username))
                    {
                        string token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_username}:{_password}"));
                        msg.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
                    }

                    using var resp = await Http.SendAsync(msg, ct).ConfigureAwait(false);
                    string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                    if (resp.IsSuccessStatusCode)
                        return EadaptorResponse.FromXml(body, (int)resp.StatusCode, transportOk: true);

                    last = new EadaptorResponse
                    {
                        TransportOk = false,
                        HttpStatus = (int)resp.StatusCode,
                        RawResponse = body,
                        Error = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}"
                    };
                    // Retry only transient server-side statuses; 4xx (auth/bad request) is permanent.
                    if (!IsTransient((int)resp.StatusCode)) return last;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return new EadaptorResponse { TransportOk = false, Error = "Cancelled." };
                }
                catch (OperationCanceledException)
                {
                    last = new EadaptorResponse { TransportOk = false, Error = "Request timed out." }; // HttpClient timeout
                }
                catch (HttpRequestException ex)
                {
                    last = new EadaptorResponse { TransportOk = false, Error = ex.Message };
                }
                catch (Exception ex)
                {
                    return new EadaptorResponse { TransportOk = false, Error = ex.Message }; // unexpected - don't retry
                }

                if (attempt < MaxAttempts)
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct).ConfigureAwait(false); // linear backoff
            }
            return last;
        }

        private static bool IsTransient(int status) => status == 408 || status == 429 || status >= 500;

        /// <summary>Lightweight connectivity/auth check using a deliberately invalid tiny document.</summary>
        public async Task<EadaptorResponse> TestConnectionAsync(CancellationToken ct = default)
        {
            // A well-formed but minimal Native doc; CargoWise will respond (usually ERR) which proves
            // the endpoint + credentials work end-to-end.
            const string probe =
                "<Native xmlns=\"http://www.cargowise.com/Schemas/Native/2011/11\" version=\"2.0\">" +
                "<Header><OwnerCode>PING</OwnerCode></Header><Body><Organization version=\"2.0\">" +
                "<OrgHeader Action=\"MERGE\"><Code>__PING__</Code></OrgHeader></Organization></Body></Native>";
            return await SendAsync(probe, ct).ConfigureAwait(false);
        }
    }
}
