using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Atlassian.Stash;
using Newtonsoft.Json;

namespace TFSToBB
{
    class Program
    {
        static void Main(string[] args)
        {
            // ExportRepos();
            //DeleteStashRepos();

            Console.WriteLine("Finished...");
            Console.ReadLine();
        }

        private const string TfsCollection = "CAE";
        private const string StashProjectKey = "CAE";
        private const string TfsProjectName = "CaeSuite";
        private const string StashUrl = "https://deltabb.cadsindia.com:8443";
        private const string StashUserName = "GowthamanS";
        private const string StashPassword = "";
        private const string TfsAccessToken = "je6ajqmbe2znpzv5ylmze2cd35xuvfhk3xmo6g277hosaffi4lja";

        private static async void ExportRepos()
        {
            var repositories = await GetRepositories();
            var tfsRepos = repositories.Where(x => x.Project.Name.Equals(TfsProjectName));

            var stashClient = new StashClient(StashUrl, StashUserName, StashPassword);
            var projects = stashClient.Projects.Get().Result;

            var caeProj = projects.Values.FirstOrDefault(x => x.Name.Equals(StashProjectKey));
            if (caeProj != null)
            {
                var stashRepos = await stashClient.Repositories.Get(StashProjectKey);

                foreach (var repository in tfsRepos)
                {
                    if (stashRepos.Values.Any(x => x.Name.Equals(repository.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    var newRepo = new Atlassian.Stash.Entities.Repository
                    {
                        Name = repository.Name,
                        Project = caeProj,
                        Public = false
                    };
                    await stashClient.Repositories.Create(StashProjectKey, newRepo);
                    CloneRepo(repository, $"{StashUrl}/scm/{StashProjectKey.ToLowerInvariant()}/{repository.Name.ToLowerInvariant()}.git");
                }
            }
        }

        private static async void DeleteStashRepos()
        {
            var stashClient = new StashClient(StashUrl, StashUserName, StashPassword);
            var stashRepos = await stashClient.Repositories.Get(StashProjectKey);

            foreach (var repo in stashRepos.Values)
            {
                // if (repo.Name.Equals("AnalysisA3DMax")) continue;
                await stashClient.Repositories.Delete(StashProjectKey, repo.Slug);
            }

        }


        private static void CloneRepo(Repository tfsRepo, string stashUrl)
        {
            Console.WriteLine($"Cloning repo {tfsRepo.Name} to {stashUrl}");
            var curDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            CallGit($"clone --mirror \"{tfsRepo.RemoteUrl}\" {tfsRepo.Name}", curDir);
            CallGit($"remote add stashRepo {stashUrl}", Path.Combine(curDir, tfsRepo.Name));
            CallGit($"push -u stashRepo --all", Path.Combine(curDir, tfsRepo.Name));
        }

        private static void CallGit(string args, string workingDir)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = @"C:\Program Files\Git\bin\git.exe",
                CreateNoWindow = true,
                Arguments = args,
                WorkingDirectory = workingDir
            };
            var process = Process.Start(startInfo);
            process?.WaitForExit();
        }

        private static async Task<List<Repository>> GetRepositories()
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{""}:{TfsAccessToken}")));

                using (var response = client.GetAsync($"https://delta.cadsindia.com/tfs/{TfsCollection}/_apis/git/repositories?api-version=1.0").Result)
                {
                    response.EnsureSuccessStatusCode();
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var obj = JsonConvert.DeserializeObject<RootObject>(responseBody);
                    //obj?.Value.ForEach(x =>
                    //{
                    //    Console.WriteLine($"{x.Name,-45}{x.RemoteUrl}");
                    //});
                    return obj?.Value;
                }
            }
        }
    }

    public class Project
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public string State { get; set; }
        public int Revision { get; set; }
        public string Visibility { get; set; }
    }

    public class Repository
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public Project Project { get; set; }
        public string DefaultBranch { get; set; }
        public string RemoteUrl { get; set; }
    }

    public class RootObject
    {
        public List<Repository> Value { get; set; }
        public int Count { get; set; }
    }
}
