﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Diagnostics.DataProviders;
using Diagnostics.Logger;
using Diagnostics.ModelsAndUtils;
using Diagnostics.ModelsAndUtils.Models;
using Diagnostics.ModelsAndUtils.ScriptUtilities;
using Diagnostics.RuntimeHost.Services;
using Diagnostics.RuntimeHost.Services.SourceWatcher;
using Diagnostics.RuntimeHost.Utilities;
using Diagnostics.Scripts;
using Diagnostics.Scripts.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Diagnostics.RuntimeHost.Controllers
{
    [Produces("application/json")]
    [Route(UriElements.SitesResource + UriElements.Diagnostics)]
    public class SitesController : SiteControllerBase
    {
        private ICompilerHostClient _compilerHostClient;
        private ISourceWatcherService _sourceWatcherService;
        private ICache<string, EntityInvoker> _invokerCache;
        private IDataSourcesConfigurationService _dataSourcesConfigService;

        public SitesController(ICompilerHostClient compilerHostClient, ISourceWatcherService sourceWatcherService, ICache<string, EntityInvoker> invokerCache, IResourceService resourceService, IDataSourcesConfigurationService dataSourcesConfigService)
            : base(resourceService)
        {
            _compilerHostClient = compilerHostClient;
            _sourceWatcherService = sourceWatcherService;
            _invokerCache = invokerCache;
            _dataSourcesConfigService = dataSourcesConfigService;
        }

        [HttpPost(UriElements.Query)]
        public async Task<IActionResult> Post(string subscriptionId, string resourceGroupName, string siteName, string[] hostNames, string stampName, [FromBody]JToken jsonBody, string startTime = null, string endTime = null, string timeGrain = null)
        {
            if (jsonBody == null)
            {
                return BadRequest("Missing body");
            }

            string script = jsonBody.Value<string>("script");

            if (string.IsNullOrWhiteSpace(script))
            {
                return BadRequest("Missing script from body");
            }

            if (!VerifyQueryParams(hostNames, stampName, out string verficationOutput))
            {
                return BadRequest(verficationOutput);
            }

            if (!DateTimeHelper.PrepareStartEndTimeWithTimeGrain(startTime, endTime, timeGrain, out DateTime startTimeUtc, out DateTime endTimeUtc, out TimeSpan timeGrainTimeSpan, out string errorMessage))
            {
                return BadRequest(errorMessage);
            }

            this.Request.Headers.TryGetValue(HeaderConstants.RequestIdHeaderName, out StringValues requestIds);

            EntityMetadata metaData = new EntityMetadata(script);
            var dataProviders = new DataProviders.DataProviders(_dataSourcesConfigService.Config);
            SiteResource resource = await _resourceService.GetSite(subscriptionId, resourceGroupName, siteName, hostNames, stampName, startTimeUtc, endTimeUtc);
            OperationContext cxt = PrepareContext(resource, startTimeUtc, endTimeUtc);

            QueryResponse<DiagnosticApiResponse> queryRes = new QueryResponse<DiagnosticApiResponse>
            {
                InvocationOutput = new DiagnosticApiResponse()
            };

            Assembly tempAsm = null;
            var compilerResponse = await _compilerHostClient.GetCompilationResponse(script, requestIds.FirstOrDefault() ?? string.Empty);

            queryRes.CompilationOutput = compilerResponse;

            if (queryRes.CompilationOutput.CompilationSucceeded)
            {
                byte[] asmData = Convert.FromBase64String(compilerResponse.AssemblyBytes);
                byte[] pdbData = Convert.FromBase64String(compilerResponse.PdbBytes);

                tempAsm = Assembly.Load(asmData, pdbData);

                using (var invoker = new EntityInvoker(metaData, ScriptHelper.GetFrameworkReferences(), ScriptHelper.GetFrameworkImports()))
                {
                    invoker.InitializeEntryPoint(tempAsm);
                    queryRes.InvocationOutput.Metadata = invoker.EntryPointDefinitionAttribute;
                    var invocationResponse = (Response)await invoker.Invoke(new object[] { dataProviders, cxt, queryRes.InvocationOutput });
                    queryRes.InvocationOutput = DiagnosticApiResponse.FromCsxResponse(invocationResponse);
                }
            }

            return Ok(queryRes);
        }

        [HttpGet(UriElements.Detectors)]
        public async Task<IActionResult> ListDetectors(string subscriptionId, string resourceGroupName, string siteName)
        {
            await _sourceWatcherService.Watcher.WaitForFirstCompletion();
            IEnumerable<DiagnosticApiResponse> entityDefinitions = _invokerCache.GetAll().Select(p => new DiagnosticApiResponse { Metadata = p.EntryPointDefinitionAttribute });
            return Ok(entityDefinitions);
        }

        [HttpGet(UriElements.Detectors + UriElements.DetectorResource)]
        public async Task<IActionResult> GetDetectorResource(string subscriptionId, string resourceGroupName, string siteName, string detectorId, string[] hostNames, string stampName, string startTime = null, string endTime = null, string timeGrain = null)
        {
            if (!VerifyQueryParams(hostNames, stampName, out string verficationOutput))
            {
                return BadRequest(verficationOutput);
            }

            if (!DateTimeHelper.PrepareStartEndTimeWithTimeGrain(startTime, endTime, timeGrain, out DateTime startTimeUtc, out DateTime endTimeUtc, out TimeSpan timeGrainTimeSpan, out string errorMessage))
            {
                return BadRequest(errorMessage);
            }

            var dataProviders = new DataProviders.DataProviders(_dataSourcesConfigService.Config);
            SiteResource resource = await _resourceService.GetSite(subscriptionId, resourceGroupName, siteName, hostNames, stampName, startTimeUtc, endTimeUtc);
            OperationContext cxt = PrepareContext(resource, startTimeUtc, endTimeUtc);

            if (!_invokerCache.TryGetValue(detectorId, out EntityInvoker invoker))
            {
                return NotFound();
            }

            Response res = new Response
            {
                Metadata = invoker.EntryPointDefinitionAttribute
            };

            var response = (Response)await invoker.Invoke(new object[] { dataProviders, cxt, res });
            var apiResponse = DiagnosticApiResponse.FromCsxResponse(response);

            return Ok(res);
        }
    }
}