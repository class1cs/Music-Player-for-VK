using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using DevExpress.Mvvm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PropertyChanged;
using VkNet;
using VkNet.AudioBypassService.Extensions;
using VkNet.Model;
using NAudio;
using NAudio.Wave;
using System.Windows.Media;
using System.Threading;
using System.Windows.Threading;
using VkNet.Enums.Filters;

namespace WpfApp21.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        public string PlayerHeight { get; set; } = "0"; // Показ плеера
        public string DownloadingHeight { get; set; } = "0"; // Показ меню скачивания
        public string DownloadingTrack { get; set; }
        public VkNet.Utils.VkCollection<VkNet.Model.Attachments.Audio> AudioInfo { get; set; }
        public VkApi Api = new VkApi(new ServiceCollection().AddAudioBypass());
        public int Progress { get; set; }
        public string PlayingSong { get; set; } 
        public string SearchRequest { get; set; }
        public string PauseOrPlay { get; set; } = "Stop"; // текст на кнопке и ее функционал
        public bool EnableSlider { get; set; } = true;
        public string Time { get; set; }
        public double CurrentTime { get; set; }
        public double SongLength { get; set; }
        public bool Dragging; // перематывается ли трек
        public bool ContinueSong; // начинать трек заново, или же продолжить проигрывание
        private readonly DispatcherTimer Timer = new DispatcherTimer();
        private MediaFoundationReader mf;
        private WaveOutEvent wo;
        private string url;
        public MainViewModel()
        {
            Timer.Interval = TimeSpan.FromMilliseconds(200);
            Timer.Tick += TimerOnTick;
            Task.Run(async () =>
            {

                try
                {

                    await Api.AuthorizeAsync(new ApiAuthParams
                    {
                        AccessToken = Properties.Settings.Default.token,
                        IsTokenUpdateAutomatically = true,

                    });

                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                }
            });
        }
        private void TimerOnTick(object sender, EventArgs eventArgs)
        {
            try
            {
                if (!Dragging)
                {
                    CurrentTime = (int)mf.CurrentTime.TotalSeconds;
                    var seconds = Math.Round(Convert.ToDouble(mf.CurrentTime.Seconds));
                    var span = new TimeSpan(mf.CurrentTime.Days, mf.CurrentTime.Hours, mf.CurrentTime.Minutes, (int)seconds);
                    Time = $"{span.ToString("mm':'ss")}{Environment.NewLine}{mf.TotalTime.ToString("mm':'ss")}";
                }

            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }

        }
        public ICommand HidePlayer => new DelegateCommand(async () =>
        {
            wo.Dispose();
            EnableSlider = true;
            await mf.DisposeAsync();
            ContinueSong = true;
            PauseOrPlay = "Play";
            PlayerHeight = "0";
        });
        public ICommand ChangePosition => new DelegateCommand<int>(async (value) =>
        {
            if ((int)CurrentTime != (int)mf.CurrentTime.TotalSeconds & wo.PlaybackState != PlaybackState.Stopped)
            {
                Dragging = true;
                mf.CurrentTime = TimeSpan.FromSeconds(value);
                var seconds = Math.Round(Convert.ToDouble(mf.CurrentTime.Seconds));
                var span = new TimeSpan(mf.CurrentTime.Days, mf.CurrentTime.Hours, mf.CurrentTime.Minutes, (int)seconds);
                Time = $"{span.ToString("mm':'ss")}{Environment.NewLine}{mf.TotalTime.ToString("mm':'ss")}";

                Dragging = false;
            }
        });
        public ICommand StopOrPlayCommand => new DelegateCommand(async () =>
        {
            try
            {
                if (PauseOrPlay == "Stop")
                {
                    wo.Pause();
                    ContinueSong = true;
                    PauseOrPlay = "Play";

                }
                else if (PauseOrPlay == "Play")
                {
                    if (ContinueSong)
                    {
                        wo.Play();
                        Timer.Start();
                        PauseOrPlay = "Stop";

                    }
                    else
                    {
                        EnableSlider = true;
                        mf.CurrentTime = TimeSpan.Zero;
                        wo.Play();
                        Timer.Start();
                        PauseOrPlay = "Stop";
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }
        });
        public Task PlayAudio()
        {
            return Task.Run(() =>
            {
                wo.Init(mf);
                wo.Play();

            });
        }
        public async Task<string> GetAudioUrl(VkNet.Model.Attachments.Audio AudioToGet)
        {
            string[] SongId = { $"{AudioToGet.OwnerId}_{AudioToGet.Id}" };
            IEnumerable<VkNet.Model.Attachments.Audio> FindedAudio = await Api.Audio.GetByIdAsync(SongId);
            return FindedAudio.FirstOrDefault().Url.ToString();
        }
        public void StopAllAudios()
        {
            wo?.Dispose();
            EnableSlider = true;
            mf?.Dispose();
        }
        public ICommand PlayCommand => new AsyncCommand<VkNet.Model.Attachments.Audio>(async (songToPlay) =>
        {
            try
            {
                StopAllAudios();
                url = await GetAudioUrl(songToPlay);
                mf = new MediaFoundationReader(url);
                wo = new WaveOutEvent();
                wo.PlaybackStopped += (s, e) =>
                {
                    if (wo.PlaybackState == PlaybackState.Stopped)
                    {
                        PauseOrPlay = "Play"; ContinueSong = false; EnableSlider = false;
                        var seconds = Math.Round(Convert.ToDouble(mf.CurrentTime.Seconds));
                        var span = new TimeSpan(mf.CurrentTime.Days, mf.CurrentTime.Hours, mf.CurrentTime.Minutes, (int)seconds);
                        Time = $"{span.ToString("mm':'ss")}{Environment.NewLine}{mf.TotalTime.ToString("mm':'ss")}";
                    }
                };
                await PlayAudio();
                OpenPlayer(songToPlay);
                await App.Current.MainWindow.Dispatcher.InvokeAsync(() =>
                {
                    App.Current.MainWindow.Closed += async (s, e) =>
                    {
                        wo.Dispose();
                        await mf.DisposeAsync();

                    };
                });
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        });
        public void OpenPlayer(VkNet.Model.Attachments.Audio CurrentTrack)
        {
            Timer.Start();
            PlayingSong = $"{CurrentTrack.Artist}{Environment.NewLine}{CurrentTrack.Title}";
            PauseOrPlay = "Stop";
            SongLength = mf.TotalTime.TotalSeconds;
            EnableSlider = true;
            PlayerHeight = "Auto";
        }
        private async Task<VkNet.Utils.VkCollection<VkNet.Model.Attachments.Audio>> MusicSearch(string searchQuery)
        {
            try
            {
                return await Api.Audio.SearchAsync(new VkNet.Model.RequestParams.AudioSearchParams { Query = searchQuery, Count = 300, Autocomplete = true });

            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return null;
            }
        }
        public ICommand SearchCommand => new AsyncCommand<string>(async (searchRequest) =>
        {
            AudioInfo = await MusicSearch(searchRequest);
            if (AudioInfo.TotalCount == 0)
            {
                MessageBox.Show($"Ничего не найдено по запросу \"{searchRequest}\"!");
            }
        }, (SearchQuery) => !string.IsNullOrEmpty(SearchRequest));
        public ICommand DownloadCommand => new AsyncCommand<VkNet.Model.Attachments.Audio>(async (song) =>
        {
            try
            {
                if (song != null)
                {
                    var id = new string[] { $"{song.OwnerId}_{song.Id}" };
                    var findedAudio = await Api.Audio.GetByIdAsync(id);
                    var opd = new SaveFileDialog
                    {
                        FileName = $"{song.Artist} - {song.Title}.mp3"
                    };
                    var singleFinded = findedAudio.FirstOrDefault();
                    if (opd.ShowDialog() == true)
                    {
                        DownloadingTrack = $"{song.Artist} - {song.Title}";
                        DownloadingHeight = "31.92";
                        using WebClient webClient = new WebClient();
                        webClient.DownloadProgressChanged += async (s, e) =>
                        {
                            Progress = e.ProgressPercentage;
                            if (e.ProgressPercentage == 100)
                            {
                                MessageBox.Show($"Скачивание трека \"{song.Artist} - {song.Title}\" успешно завершено!");
                                DownloadingHeight = "0";
                            }
                        };
                        webClient.DownloadFileAsync(singleFinded.Url, $"{opd.FileName}.mp3");
                        webClient.DownloadProgressChanged -= null;
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }
        });
    }
}
