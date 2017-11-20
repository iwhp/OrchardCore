using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;
using OrchardCore.Modules;

namespace OrchardCore.Mvc
{
    /// <summary>
    /// This custom <see cref="IFileProvider"/> implementation provides the file contents
    /// of Module Project Razor files while in a development environment.
    /// </summary>
    public class ModuleProjectRazorFileProvider : IFileProvider
    {
        private static Dictionary<string, string> _paths;
        private static CompositeFileProvider _pagesFileProvider;
        private static object _synLock = new object();

        public ModuleProjectRazorFileProvider(IHostingEnvironment environment)
        {
            if (_paths != null)
            {
                return;
            }

            lock (_synLock)
            {
                if (_paths == null)
                {
                    var paths = new List<string>();
                    var mainAssembly = environment.LoadApplicationAssembly();

                    foreach (var moduleId in environment.GetModuleNames())
                    {
                        var assembly = environment.LoadModuleAssembly(moduleId);

                        if (assembly == null || Path.GetDirectoryName(assembly.Location)
                            != Path.GetDirectoryName(mainAssembly.Location))
                        {
                            continue;
                        }

                        var assetPaths = environment.GetModuleAssets(moduleId);
                        var projectFolder = assetPaths.FirstOrDefault();

                        if (Directory.Exists(projectFolder))
                        {
                            assetPaths = assetPaths.Skip(1).Where(x => x.EndsWith(".cshtml")).ToList();

                            paths.AddRange(assetPaths.Select(x => projectFolder + "/"
                                + x.Substring(("Modules/" + moduleId).Length) + "|/" + x));
                        }
                    }

                    var map = new Dictionary<string, string>(paths
                        .Select(x => x.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                        .Where(x => x.Length == 2).ToDictionary(x => x[1], x => x[0]));

                    var roots = new HashSet<string>();

                    foreach (var path in map.Values.Where(p => p.Contains("/Pages/") && !p.StartsWith("/Pages/")))
                    {
                        roots.Add(path.Substring(0, path.IndexOf("/Pages/")));
                    }

                    if (roots.Count > 0)
                    {
                        _pagesFileProvider = new CompositeFileProvider(roots.Select(r => new PhysicalFileProvider(r)));
                    }

                    _paths = map;
                }
            }
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            return NotFoundDirectoryContents.Singleton;
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            if (subpath != null && _paths.ContainsKey(subpath))
            {
                return new PhysicalFileInfo(new FileInfo(_paths[subpath]));
            }

            return new NotFoundFileInfo(subpath);
        }

        public IChangeToken Watch(string filter)
        {
            if (filter != null && _paths.ContainsKey(filter))
            {
                return new PollingFileChangeToken(new FileInfo(_paths[filter]));
            }

            if (filter != null && _pagesFileProvider != null &&
                filter.IndexOf("/Pages/**/*" + RazorViewEngine.ViewExtension) != -1)
            {
                return _pagesFileProvider.Watch("/Pages/**/*" + RazorViewEngine.ViewExtension);
            }

            return NullChangeToken.Singleton;
        }
    }
}
