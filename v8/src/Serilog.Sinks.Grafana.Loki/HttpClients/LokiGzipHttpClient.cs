// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Net.Http.Headers;

namespace Serilog.Sinks.Grafana.Loki.HttpClients;

/// <summary>
/// Http client with gzip compression used for sending log events to Grafana Loki.
/// </summary>
public class LokiGzipHttpClient : BaseLokiHttpClient
{
    /// <summary>
    /// <see cref="CompressionLevel"/> used for compression.
    /// </summary>
    protected readonly CompressionLevel CompressionLevel;

    /// <summary>
    /// Initializes a new instance of the <see cref="LokiHttpClient"/> class.
    /// </summary>
    /// <param name="httpClient">
    /// <see cref="HttpClient"/> be used for HTTP requests.
    /// </param>
    /// <param name="compressionLevel">
    /// <see cref="CompressionLevel"/> be used for HTTP requests.
    /// </param>
    public LokiGzipHttpClient(
        HttpClient? httpClient = null,
        CompressionLevel compressionLevel = CompressionLevel.Fastest)
        : base(httpClient)
    {
        CompressionLevel = compressionLevel;
    }

    /// <inheritdoc/>
    public override async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream)
    {
        using var output = new MemoryStream();

        using (var gzipStream = new GZipStream(output, CompressionLevel, true))
        {
            await contentStream.CopyToAsync(gzipStream).ConfigureAwait(false);
        }

        output.Position = 0;

        using (var content = new StreamContent(output))
        {
            content.Headers.ContentEncoding.Add("gzip");
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            return await HttpClient.PostAsync(requestUri, content).ConfigureAwait(false);
        }
    }
}