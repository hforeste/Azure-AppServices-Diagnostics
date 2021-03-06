﻿using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Diagnostics.DataProviders
{
    [DataSourceConfiguration("Kusto")]
    public class KustoDataProviderConfiguration : IDataProviderConfiguration
    {
        /// <summary>
        /// Client Id
        /// </summary>
        [ConfigurationName("ClientId")]
        public string ClientId { get; set; }

        /// <summary>
        /// App Key
        /// </summary>
        [ConfigurationName("AppKey")]
        public string AppKey { get; set; }

        /// <summary>
        /// DB Name
        /// </summary>
        [ConfigurationName("DBName")]
        public string DBName { get; set; }

        /// <summary>
        /// DB Name
        /// </summary>
        [ConfigurationName("KustoRegionGroupings")]
        public string KustoRegionGroupings { get; set; }

        /// <summary>
        /// DB Name
        /// </summary>
        [ConfigurationName("KustoClusterNameGroupings")]
        public string KustoClusterNameGroupings { get; set; }

        /// <summary>
        /// DB Name
        /// </summary>
        [ConfigurationName("KustoClusterFailoverGroupings")]
        public string KustoClusterFailoverGroupings { get; set; }

        /// <summary>
        /// Tenant to authenticate with
        /// </summary>
        [ConfigurationName("AADAuthority")]
        public string AADAuthority { get; set; }

        /// <summary>
        /// Resource to issue token
        /// </summary>
        [ConfigurationName("AADKustoResource")]
        public string AADKustoResource { get; set; }

        /// <summary>
        /// Number of consecutive failures before failing over to the fail over cluster.
        /// </summary>
        [ConfigurationName("HeartBeatConsecutiveFailureLimit")]
        public int HeartBeatConsecutiveFailureLimit { get; set; }

        /// <summary>
        /// Number of consecutive successes before returning to the primary cluster.
        /// </summary>
        [ConfigurationName("HeartBeatConsecutiveSuccessLimit")]
        public int HeartBeatConsecutiveSuccessLimit { get; set; }

        /// <summary>
        /// Query to run against each cluster to check health
        /// </summary>
        [ConfigurationName("HeartBeatQuery")]
        public string HeartBeatQuery { get; set; }

        /// <summary>
        /// Timeout of the query
        /// </summary>
        [ConfigurationName("HeartBeatTimeOutInSeconds")]
        public int HeartBeatTimeOutInSeconds { get; set; }

        /// <summary>
        /// Delay between each heart beat
        /// </summary>
        [ConfigurationName("HeartBeatDelayInSeconds")]
        public int HeartBeatDelayInSeconds { get; set; }

        /// <summary>
        /// Region Specific Cluster Names.
        /// </summary>
        public ConcurrentDictionary<string, string> RegionSpecificClusterNameCollection { get; set; }

        /// <summary>
        /// Failover Cluster Names.
        /// </summary>
        public ConcurrentDictionary<string, string> FailoverClusterNameCollection { get; set; }

        public string CloudDomain
        {
            get
            {
                if (AADKustoResource.Contains("windows.net"))
                {
                    return DataProviderConstants.AzureCloud;
                }
                else if (AADKustoResource.Contains("chinacloudapi.cn"))
                {
                    return DataProviderConstants.AzureChinaCloud;
                }
                else
                {
                    return DataProviderConstants.AzureUSGovernment;
                }
            }
        }

        public string KustoApiEndpoint
        {
            get
            {
                var m = Regex.Match(AADKustoResource, @"https://(?<cluster>\w+).");
                if (m.Success)
                {
                    return AADKustoResource.Replace(m.Groups["cluster"].Value, "{cluster}");
                }
                else
                {
                    throw new ArgumentException(nameof(AADKustoResource) + " not correctly formatted.");
                }
            }
        }

        public void PostInitialize()
        {
            RegionSpecificClusterNameCollection = new ConcurrentDictionary<string, string>();
            FailoverClusterNameCollection = new ConcurrentDictionary<string, string>();

            if (string.IsNullOrWhiteSpace(KustoRegionGroupings) && string.IsNullOrWhiteSpace(KustoClusterNameGroupings))
            {
                return;
            }

            var separator = new char[] { ',' };
            var regionGroupingParts = KustoRegionGroupings.Split(separator);
            var clusterNameGroupingParts = KustoClusterNameGroupings.Split(separator);
            var clusterFailoverGroupingParts = string.IsNullOrWhiteSpace(KustoClusterFailoverGroupings) ? new string[0] : KustoClusterFailoverGroupings.Split(separator);

            if (regionGroupingParts.Length != clusterNameGroupingParts.Length)
            {
                // TODO: Log
                return;
            }

            for (int iterator = 0; iterator < regionGroupingParts.Length; iterator++)
            {
                var regionParts = regionGroupingParts[iterator].Split(new char[] { ':' });

                foreach (var region in regionParts)
                {
                    if (!String.IsNullOrWhiteSpace(region))
                    {
                        RegionSpecificClusterNameCollection.TryAdd(region.ToLower(), clusterNameGroupingParts[iterator]);
                    }
                }

                if (iterator < clusterFailoverGroupingParts.Length && !String.IsNullOrWhiteSpace(clusterFailoverGroupingParts[iterator]))
                {
                    FailoverClusterNameCollection.TryAdd(clusterNameGroupingParts[iterator], clusterFailoverGroupingParts[iterator]);
                }
            }
        }
    }
}
