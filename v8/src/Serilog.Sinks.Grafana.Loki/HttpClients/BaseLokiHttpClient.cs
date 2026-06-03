// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace Serilog.Sinks.Grafana.Loki.HttpClients;

/// <summary>
/// Base http client for sending log events to Grafana Loki.
/// Implements method for sending authorization header
/// </summary>
public abstract class BaseLokiHttpClient : ILokiHttpClient
{
    /// <summary>
    /// <see cref="HttpClient"/> used for requests.
    /// </summary>
    protected readonly HttpClient HttpClient;

    /// <summary>
    /// Header used for passing tenant ID. See <a href="https://grafana.com/docs/loki/latest/operations/multi-tenancy/">docs</a>.
    /// </summary>
    private const string TenantHeader = "X-Scope-OrgID";

    /// <summary>
    /// Regex for Tenant ID validation.
    /// </summary>
    private static readonly Regex TenantIdValueRegex = new(@"^(?!.*\.\.)(?!\.$)[a-zA-Z0-9!._*'()\-\u005F]*$", RegexOptions.Compiled);

    /// <summary>
    /// RFC7230 token characters: letters, digits and these symbols: ! # $ % &amp; ' * + - . ^ _ ` | ~
    /// </summary>
    private static readonly Regex HeaderKeyRegEx = new(@"^[A-Za-z0-9!#$%&'*+\-\.\^_`|~]+$", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseLokiHttpClient"/> class.
    /// </summary>
    /// <param name="httpClient">
    /// <see cref="HttpClient"/> be used for HTTP requests.
    /// </param>
    protected BaseLokiHttpClient(HttpClient? httpClient = null)
    {
        HttpClient = httpClient ?? new HttpClient();
    }

    /// <inheritdoc/>
    public abstract Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream);

    /// <inheritdoc/>
    public virtual void SetCredentials(LokiCredentials? credentials)
    {
        if (credentials == null || credentials.IsEmpty)
        {
            return;
        }

        var headers = HttpClient.DefaultRequestHeaders;

        if (headers.Any(h => h.Key == "Authorization"))
        {
            return;
        }

        var token = Base64Encode($"{credentials.Login}:{credentials.Password ?? string.Empty}");
        headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    /// <inheritdoc/>
    public virtual void SetTenant(string? tenant)
    {
        if (string.IsNullOrEmpty(tenant))
        {
            return;
        }

        if (!TenantIdValueRegex.IsMatch(tenant))
        {
            throw new ArgumentException($"{tenant} argument does not follow rule for Tenant ID", nameof(tenant));
        }

        var headers = HttpClient.DefaultRequestHeaders;

        if (headers.Any(h => h.Key == TenantHeader))
        {
            return;
        }

        headers.Add(TenantHeader, tenant);
    }

    /// <summary>
    /// Sets default headers for the HTTP client.
    /// Existing headers with the same key will not be overwritten.
    /// </summary>
    /// <param name="defaultHeaders">A dictionary of headers to set as default.</param>
    public virtual void SetDefaultHeaders(IDictionary<string, string> defaultHeaders)
    {
        if (defaultHeaders == null)
        {
            throw new ArgumentNullException(nameof(defaultHeaders), "Default headers cannot be null.");
        }

        foreach (var header in defaultHeaders)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                throw new ArgumentException("Header name cannot be null, empty, or whitespace.", nameof(defaultHeaders));
            }

            if (!HeaderKeyRegEx.IsMatch(header.Key))
            {
                throw new ArgumentException($"Header name '{header.Key}' contains invalid characters.", nameof(defaultHeaders));
            }

            if (header.Value == null)
            {
                throw new ArgumentException($"Header value for '{header.Key}' cannot be null.", nameof(defaultHeaders));
            }

            if (header.Value.Length == 0)
            {
                throw new ArgumentException($"Header value for '{header.Key}' cannot be empty.", nameof(defaultHeaders));
            }

            if (!HttpClient.DefaultRequestHeaders.Contains(header.Key))
            {
                HttpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        }
    }

    /// <inheritdoc/>
    public virtual void Dispose() => HttpClient.Dispose();

    private static string Base64Encode(string str) => Convert.ToBase64String(Encoding.UTF8.GetBytes(str));
}