﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Hosting;
using Talifun.Web.Helper;
using Yahoo.Yui.Compressor;

namespace Talifun.Web.Crusher
{
    /// <summary>
    /// Manages the adding and removing of css files to crush. It also does the css crushing.
    /// </summary>
    public class CssCrusher : ICssCrusher
    {
        protected readonly IRetryableFileOpener RetryableFileOpener;
        protected readonly IRetryableFileWriter RetryableFileWriter;
        protected readonly ICssPathRewriter CssPathRewriter;

        public CssCrusher(IRetryableFileOpener retryableFileOpener, IRetryableFileWriter retryableFileWriter, ICssPathRewriter cssPathRewriter)
        {
            RetryableFileOpener = retryableFileOpener;
            RetryableFileWriter = retryableFileWriter;
            CssPathRewriter = cssPathRewriter;
        }

        /// <summary>
        /// Add css files to be crushed.
        /// </summary>
        /// <param name="outputPath">The path for the crushed css file.</param>
        /// <param name="files">The css files to be crushed.</param>
        public virtual void AddFiles(string outputPath, IEnumerable<CssFile> files)
        {
            var crushedContent = ProcessFiles(outputPath, files);
            RetryableFileWriter.SaveContentsToFile(crushedContent, outputPath);
            AddFilesToCache(outputPath, files);
        }

        /// <summary>
        /// Compiles dot less css file contents.
        /// </summary>
        /// <param name="fileContents">Uncompiled css file contents.</param>
        /// <returns>Compiled css file contents.</returns>
        public virtual string ProcessDotLess(string fileContents)
        {
            return dotless.Core.Less.Parse(fileContents);
        }

        /// <summary>
        /// Compress the css files and store them in the specified css file.
        /// </summary>
        /// <param name="outputPath">The path for the crushed css file.</param>
        /// <param name="files">The css files to be crushed.</param>
        public virtual StringBuilder ProcessFiles(string outputPath, IEnumerable<CssFile> files)
        {
            outputPath = HostingEnvironment.MapPath(outputPath);
            var uncompressedContents = new StringBuilder();
            var toBeStockYuiCompressedContents = new StringBuilder();
            var toBeMichaelAshRegexCompressedContents = new StringBuilder();
            var toBeHybridCompressedContents = new StringBuilder();

            foreach (var file in files)
            {
                var filePath = HostingEnvironment.MapPath(file.FilePath);
                var fileInfo = new FileInfo(filePath);
                var fileContents = RetryableFileOpener.ReadAllText(fileInfo);
                var fileName = fileInfo.Name.ToLower();

                if (fileName.EndsWith(".less") || fileName.EndsWith(".less.css"))
                {
                    fileContents = ProcessDotLess(fileContents);
                }

                fileContents = CssPathRewriter.RewriteCssPaths(outputPath, fileInfo.FullName, fileContents);

                switch (file.CompressionType)
                {
                    case CssCompressionType.None:
                        uncompressedContents.AppendLine(fileContents);
                        break;
                    case CssCompressionType.StockYuiCompressor:
                        toBeStockYuiCompressedContents.AppendLine(fileContents);
                        break;
                    case CssCompressionType.MichaelAshRegexEnhancements:
                        toBeMichaelAshRegexCompressedContents.AppendLine(fileContents);
                        break;
                    case CssCompressionType.Hybrid:
                        toBeHybridCompressedContents.AppendLine(fileContents);
                        break;
                }
            }

            if (toBeStockYuiCompressedContents.Length > 0)
            {
                uncompressedContents.Append(CssCompressor.Compress(toBeStockYuiCompressedContents.ToString(), 0, Yahoo.Yui.Compressor.CssCompressionType.StockYuiCompressor));
            }

            if (toBeMichaelAshRegexCompressedContents.Length > 0)
            {
                uncompressedContents.Append(CssCompressor.Compress(toBeMichaelAshRegexCompressedContents.ToString(), 0, Yahoo.Yui.Compressor.CssCompressionType.MichaelAshRegexEnhancements));
            }

            if (toBeHybridCompressedContents.Length > 0)
            {
                uncompressedContents.Append(CssCompressor.Compress(toBeHybridCompressedContents.ToString(), 0, Yahoo.Yui.Compressor.CssCompressionType.Hybrid));
            }

            return uncompressedContents;
        }


        /// <summary>
        /// Remove all css files from being crushed
        /// </summary>
        /// <param name="outputPath">The path for the crushed css file.</param>
        public virtual void RemoveFiles(string outputPath)
        {
            HttpRuntime.Cache.Remove(GetKey(outputPath));
        }

        /// <summary>
        /// Add the css files to the cache so that they are monitored for any changes.
        /// </summary>
        /// <param name="outputPath">The path for the crushed css file.</param>
        /// <param name="files">The css files to be crushed.</param>
        public virtual void AddFilesToCache(string outputPath, IEnumerable<CssFile> files)
        {
            var fileNames = new List<string>
                                {
                                    HostingEnvironment.MapPath(outputPath)
                                };

            foreach (var file in files)
            {
                fileNames.Add(HostingEnvironment.MapPath(file.FilePath));
            }

            HttpRuntime.Cache.Insert(
                GetKey(outputPath),
                files,
                new CacheDependency(fileNames.ToArray(), System.DateTime.Now),
                Cache.NoAbsoluteExpiration,
                Cache.NoSlidingExpiration,
                CacheItemPriority.High,
                FileRemoved);
        }

        /// <summary>
        /// When a file is removed from cache, keep it in the cache if it is unused or expired as we want to continue to monitor
        /// any changes to file. If it has been removed because the file has changed then regenerate the crushed css file and
        /// start the monitoring again.
        /// </summary>
        /// <param name="key">The key of the cache item.</param>
        /// <param name="value">The value of the cache item.</param>
        /// <param name="reason">The reason the file was removed from cache.</param>
        public virtual void FileRemoved(string key, object value, CacheItemRemovedReason reason)
        {
            var outputPath = GetOutputPathFromKey(key);
            var files = (List<CssFile>)value;
            switch (reason)
            {
                case CacheItemRemovedReason.DependencyChanged:
                    AddFiles(outputPath, files);
                    break;
                case CacheItemRemovedReason.Underused:
                case CacheItemRemovedReason.Expired:
                    AddFilesToCache(outputPath, files);
                    break;
            }
        }

        /// <summary>
        /// Get the cache key to use for caching.
        /// </summary>
        /// <param name="outputPath">The path for the crushed css file.</param>
        /// <returns>The cache key to use for caching.</returns>
        public virtual string GetKey(string outputPath)
        {
            var prefix = typeof (CssCrusher).ToString() + "|";
            return prefix + outputPath;
        }

        /// <summary>
        /// Get the output path from the cache key.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <returns>The path for the crushed css file.</returns>
        public virtual string GetOutputPathFromKey(string key)
        {
            var prefix = typeof(CssCrusher).ToString() + "|";
            return key.Substring(prefix.Length);
        }
    }
}