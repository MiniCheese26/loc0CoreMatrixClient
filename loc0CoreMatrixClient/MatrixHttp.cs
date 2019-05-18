using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace loc0CoreMatrixClient
{
    /// <summary>
    /// Methods for communicating with the Matrix API
    /// </summary>
    internal class MatrixHttp
    {
        private readonly HttpClient _client = new HttpClient();

        /// <summary>
        /// Wrapper for posting to a Matrix endpoint without content
        /// </summary>
        /// <param name="url">Endpoint</param>
        /// <returns>HttpResponseMessage for consumption</returns>
        public async Task<HttpResponseMessage> Post(string url)
        {
            if (!Regex.IsMatch(url, @"^https:\/\/"))
            {
                url = "https://" + url;
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(HttpUtility.UrlEncode(url))))
            {
                return await _client.SendAsync(request);
            }
        }

        /// <summary>
        /// Wrapper for posting to a Matrix endpoint with content
        /// </summary>
        /// <param name="url">Endpoint</param>
        /// <param name="content">Content to be posted</param>
        /// <param name="contentType">Content type, defaults to application/json</param>
        /// <returns>HttpResponseMessage for consumption</returns>
        public async Task<HttpResponseMessage> Post(string url, string content, string contentType = "application/json")
        {
            HttpResponseMessage response;

            if (!Regex.IsMatch(url, @"^https:\/\/"))
            {
                url = "https://" + url;
            }

            using (var request = new StringContent(content, Encoding.UTF8, contentType))
            {
                response = await _client.PostAsync(new Uri(HttpUtility.UrlEncode(url)), request);
            }

            return response;
        }

        /// <summary>
        /// Wrapper for posting to a Matrix endpoint with a byte[]
        /// </summary>
        /// <param name="url">Endpoint</param>
        /// <param name="content">Content as a byte[] to be posted</param>
        /// <param name="contentType">Content type</param>
        /// <returns>HttpResponseMessage for consumption</returns>
        public async Task<HttpResponseMessage> Post(string url, byte[] content, string contentType)
        {
            if (!Regex.IsMatch(url, @"^https:\/\/"))
            {
                url = "https://" + url;
            }

            var byteArrayContent = new ByteArrayContent(content);
            byteArrayContent.Headers.Add("Content-Type", contentType);
            HttpResponseMessage response = await _client.PostAsync(new Uri(HttpUtility.UrlEncode(url)), byteArrayContent);

            return response;
        }

        /// <summary>
        /// Wrapper for getting from a Matrix endpoint
        /// </summary>
        /// <param name="url">Endpoint</param>
        /// <returns>HttpResponseMessage for consumption</returns>
        public async Task<HttpResponseMessage> Get(string url) => await _client.GetAsync(new Uri(HttpUtility.UrlEncode(url)));

        public async Task<HttpResponseMessage> Put(string url, string content, string contentType = "application/json")
        {
            HttpResponseMessage response;

            if (!Regex.IsMatch(url, @"^https:\/\/"))
            {
                url = "https://" + url;
            }

            using (var messageContent = new StringContent(content, Encoding.UTF8, contentType))
            {
                response = await _client.PutAsync(new Uri(HttpUtility.UrlEncode(url)), messageContent);
            }

            return response;
        }
    }
}