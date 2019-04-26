using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows.Input;
using Atlassian.Stash;
using MahApps.Metro.Controls.Dialogs;
using Mvvm;
using Mvvm.Commands;
using Newtonsoft.Json;

namespace TFSToBBUI
{
    public class MainViewModel : BindableBase
    {

        public MainViewModel(IDialogCoordinator dialogCoordinator)
        {
            _dialogCoordinator = dialogCoordinator;
            LoadReposCommand = new DelegateCommand(OnLoadRepos);
            ExportCommand = new DelegateCommand<Repository>(OnExport);
            DeleteRepoCommand = new DelegateCommand<Atlassian.Stash.Entities.Repository>(OnDeleteStashRepo);
        }

        public ICommand LoadReposCommand { get; set; }
        public ICommand ExportCommand { get; set; }
        public ICommand DeleteRepoCommand { get; set; }

        public string TfsCollection { get; set; } = "CAE";
        public string TfsProjectName { get; set; } = "CaeSuite";
        public string TfsUrl { get; set; } = "https://delta.cadsindia.com/tfs";
        public string TfsAccessToken { get; set; } = "je6ajqmbe2znpzv5ylmze2cd35xuvfhk3xmo6g277hosaffi4lja";

        public string StashProjectKey { get; set; } = "CAE";
        public string StashUrl { get; set; } = "https://deltabb.cadsindia.com:8443";
        public string StashUserName { get; set; } = "GowthamanS";
        public string StashPassword { get; set; } = "";

        public Atlassian.Stash.Entities.Project StashProject { get; set; }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged(() => IsBusy);
            }
        }

        public string StausText
        {
            get => _stausText;
            set
            {
                _stausText = value;
                OnPropertyChanged(() => StausText);
            }
        }

        public List<Repository> TfsRepos
        {
            get => _tfsRepos;
            set
            {
                _tfsRepos = value;
                OnPropertyChanged(() => TfsRepos);
            }
        }

        public List<Atlassian.Stash.Entities.Repository> StashRepos
        {
            get => _stashRepos;
            set
            {
                _stashRepos = value;
                OnPropertyChanged(() => StashRepos);
            }
        }


        private List<Repository> _tfsRepos;
        private StashClient _stashClient;
        private List<Atlassian.Stash.Entities.Repository> _stashRepos;
        private IDialogCoordinator _dialogCoordinator;
        private bool _isBusy;
        private string _stausText;

        private async void OnExport(Repository repo)
        {
            if (StashRepos == null || StashProject == null)
            {
                await _dialogCoordinator.ShowMessageAsync(this, "Error", "Stash details are not initialized properly.");
            }
            else if (StashRepos.Any(x => x.Name.Equals(repo.Name, StringComparison.OrdinalIgnoreCase)))
            {
                await _dialogCoordinator.ShowMessageAsync(this, "Error", "Repository already exists in Bit bucket");
            }
            else
            {
                var newRepo = new Atlassian.Stash.Entities.Repository
                {
                    Name = repo.Name,
                    Project = StashProject,
                    Public = false
                };
                IsBusy = true;
                StausText = $"Migrating repo {repo.Name} to Bitbucket project {StashProjectKey}";
                var repoResponse = await _stashClient.Repositories.Create(StashProjectKey, newRepo);

                if (repoResponse == null)
                {
                    await _dialogCoordinator.ShowMessageAsync(this, "Error", $"Failed to create repo in Bitbucket");
                }
                else
                {
                    await Task.Factory.StartNew(() =>
                    {
                        CloneRepo(repo, $"{StashUrl}/scm/{StashProjectKey.ToLowerInvariant()}/{repo.Name.ToLowerInvariant()}.git");
                    });
                }
                IsBusy = false;
                RefreshRepos();
            }
        }

        private async void OnDeleteStashRepo(Atlassian.Stash.Entities.Repository obj)
        {
            await _stashClient.Repositories.Delete(StashProjectKey, obj.Slug);
            await _dialogCoordinator.ShowMessageAsync(this, "Repository", $"Bitbucket Repository {obj.Name} is deleted.");
            RefreshRepos();
        }

        private void OnLoadRepos()
        {
            RefreshRepos();
        }

        private async void RefreshRepos()
        {
            StausText = "Loading Repositores";
            IsBusy = true;

            try
            {
                var repos = GetRepositories().Result;
                TfsRepos = repos.Where(x => x.Project.Name.Equals(TfsProjectName, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name).ToList();

                _stashClient = new StashClient(StashUrl, StashUserName, StashPassword);
                var response = await _stashClient.Repositories.Get(StashProjectKey);
                StashRepos = response.Values.OrderBy(x => x.Name).ToList();
                var projects = _stashClient.Projects.Get().Result;
                StashProject = projects.Values.FirstOrDefault(x => x.Name.Equals(StashProjectKey));
            }
            catch (Exception e)
            {
                await _dialogCoordinator.ShowMessageAsync(this, "Error", e.ToString());
            }

            IsBusy = false;
        }

        private async Task<List<Repository>> GetRepositories()
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{TfsAccessToken}")));
                using (var response = client.GetAsync($"{TfsUrl}/{TfsCollection}/_apis/git/repositories?api-version=1.0").Result)
                {
                    response.EnsureSuccessStatusCode();
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var obj = JsonConvert.DeserializeObject<TfsCollection>(responseBody);
                    return obj?.Value;
                }
            }
        }

        private static void CloneRepo(Repository tfsRepo, string stashUrl)
        {
            var curDir = Path.GetTempPath();
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
                WorkingDirectory = workingDir,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            };
            var process = Process.Start(startInfo);
            process?.WaitForExit();
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

    public class TfsCollection
    {
        public List<Repository> Value { get; set; }
        public int Count { get; set; }
    }
}
