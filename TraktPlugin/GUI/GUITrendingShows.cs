﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MediaPortal.Configuration;
using MediaPortal.GUI.Library;
using Action = MediaPortal.GUI.Library.Action;
using MediaPortal.Util;
using TraktPlugin.TraktAPI;
using TraktPlugin.TraktAPI.DataStructures;

namespace TraktPlugin.GUI
{
    public class GUITrendingShows : GUIWindow
    {
        #region Skin Controls

        [SkinControl(2)]
        protected GUIButtonControl layoutButton = null;

        [SkinControl(8)]
        protected GUISortButtonControl sortButton = null;

        [SkinControl(50)]
        protected GUIFacadeControl Facade = null;

        [SkinControlAttribute(60)]
        protected GUIImage FanartBackground = null;

        [SkinControlAttribute(61)]
        protected GUIImage FanartBackground2 = null;

        [SkinControlAttribute(62)]
        protected GUIImage loadingImage = null;

        #endregion

        #region Enums

        enum Layout
        {
            List = 0,
            SmallIcons = 1,
            LargeIcons = 2,
            Filmstrip = 3,
        }

        enum ContextMenuItem
        {
            AddToWatchList,
            RemoveFromWatchList,
            AddToList,
            Related,
            Trailers,
            Shouts,
            Rate,
            ChangeLayout,
            SearchWithMpNZB,
            SearchTorrent
        }

        #endregion

        #region Constructor

        public GUITrendingShows() 
        {
            backdrop = new ImageSwapper();
            backdrop.PropertyOne = "#Trakt.TrendingShows.Fanart.1";
            backdrop.PropertyTwo = "#Trakt.TrendingShows.Fanart.2";
        }

        #endregion

        #region Private Variables

        bool StopDownload { get; set; }
        private Layout CurrentLayout { get; set; }
        private ImageSwapper backdrop;
        DateTime LastRequest = new DateTime();
        int PreviousSelectedIndex = 0;

        IEnumerable<TraktTrendingShow> TrendingShows
        {
            get
            {
                if (_TrendingShows == null || LastRequest < DateTime.UtcNow.Subtract(new TimeSpan(0, TraktSettings.WebRequestCacheMinutes, 0)))
                {
                    _TrendingShows = TraktAPI.TraktAPI.GetTrendingShows();
                    LastRequest = DateTime.UtcNow;
                    PreviousSelectedIndex = 0;
                }
                return _TrendingShows;
            }
        }
        private IEnumerable<TraktTrendingShow> _TrendingShows = null;
        
        #endregion

        #region Base Overrides

        public override int GetID
        {
            get
            {
                return 87265;
            }
        }

        public override bool Init()
        {
            return Load(GUIGraphicsContext.Skin + @"\Trakt.Trending.Shows.xml");
        }

        protected override void OnPageLoad()
        {
            base.OnPageLoad();

            // Clear GUI Properties
            ClearProperties();

            // Init Properties
            InitProperties();

            // Load Trending Shows
            LoadTrendingShows();
        }

        protected override void OnPageDestroy(int new_windowId)
        {
            StopDownload = true;
            PreviousSelectedIndex = Facade.SelectedListItemIndex;
            ClearProperties();

            // save current layout
            TraktSettings.TrendingShowsDefaultLayout = (int)CurrentLayout;

            base.OnPageDestroy(new_windowId);
        }

        protected override void OnClicked(int controlId, GUIControl control, Action.ActionType actionType)
        {
            // wait for any background action to finish
            if (GUIBackgroundTask.Instance.IsBusy) return;

            switch (controlId)
            {
                // Facade
                case (50):
                    if (actionType == Action.ActionType.ACTION_SELECT_ITEM)
                    {
                        CheckAndPlayEpisode(true);
                    }
                    break;

                // Layout Button
                case (2):
                    ShowLayoutMenu();
                    break;

                // Sort Button
                case (8):
                    var newSortBy = GUICommon.ShowSortMenu(TraktSettings.SortByTrendingShows);
                    if (newSortBy != null)
                    {
                        if (newSortBy.Field != TraktSettings.SortByTrendingShows.Field)
                        {
                            TraktSettings.SortByTrendingShows = newSortBy;
                            PreviousSelectedIndex = 0;
                            UpdateButtonState();
                            LoadTrendingShows();
                        }
                    }
                    break;

                default:
                    break;
            }
            base.OnClicked(controlId, control, actionType);
        }

        public override void OnAction(Action action)
        {
            switch (action.wID)
            {
                case Action.ActionType.ACTION_PLAY:
                case Action.ActionType.ACTION_MUSIC_PLAY:
                    CheckAndPlayEpisode(false);
                    break;
                default:
                    base.OnAction(action);
                    break;
            }
        }

        protected override void OnShowContextMenu()
        {
            GUIListItem selectedItem = this.Facade.SelectedListItem;
            if (selectedItem == null) return;

            TraktTrendingShow selectedShow = (TraktTrendingShow)selectedItem.TVTag;

            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            if (dlg == null) return;

            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            GUIListItem listItem = null;

            // Add/Remove Watch List            
            if (!selectedShow.InWatchList)
            {
                listItem = new GUIListItem(Translation.AddToWatchList);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.AddToWatchList;
            }
            else
            {
                listItem = new GUIListItem(Translation.RemoveFromWatchList);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.RemoveFromWatchList;
            }

            // Add to Custom List
            listItem = new GUIListItem(Translation.AddToList + "...");
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.AddToList;

            if (TraktHelper.IsOnlineVideosAvailableAndEnabled)
            {
                listItem = new GUIListItem(Translation.Trailers);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.Trailers;
            }

            // Related Shows
            listItem = new GUIListItem(Translation.RelatedShows + "...");
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.Related;

            // Rate Show
            listItem = new GUIListItem(Translation.RateShow);
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.Rate;

            // Shouts
            listItem = new GUIListItem(Translation.Shouts + "...");
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.Shouts;

            // Change Layout
            listItem = new GUIListItem(Translation.ChangeLayout);
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.ChangeLayout;

            if (TraktHelper.IsMpNZBAvailableAndEnabled)
            {
                // Search for show with mpNZB
                listItem = new GUIListItem(Translation.SearchWithMpNZB);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.SearchWithMpNZB;
            }

            if (TraktHelper.IsMyTorrentsAvailableAndEnabled)
            {
                // Search for show with MyTorrents
                listItem = new GUIListItem(Translation.SearchTorrent);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.SearchTorrent;
            }

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)ContextMenuItem.AddToWatchList):
                    AddShowToWatchList(selectedShow);
                    selectedShow.InWatchList = true;
                    OnShowSelected(selectedItem, Facade);
                    selectedShow.Images.NotifyPropertyChanged("PosterImageFilename");
                    GUIWatchListShows.ClearCache(TraktSettings.Username);
                    break;

                case ((int)ContextMenuItem.RemoveFromWatchList):
                    RemoveShowFromWatchList(selectedShow);
                    selectedShow.InWatchList = false;
                    OnShowSelected(selectedItem, Facade);
                    selectedShow.Images.NotifyPropertyChanged("PosterImageFilename");
                    GUIWatchListShows.ClearCache(TraktSettings.Username);
                    break;

                case ((int)ContextMenuItem.AddToList):
                    TraktHelper.AddRemoveShowInUserList(selectedShow.Title, selectedShow.Year.ToString(), selectedShow.Tvdb, false);
                    break;
                
                case ((int)ContextMenuItem.Related):
                    RelatedShow relatedShow = new RelatedShow();
                    relatedShow.Title = selectedShow.Title;
                    relatedShow.TVDbId = selectedShow.Tvdb;
                    GUIRelatedShows.relatedShow = relatedShow;
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.RelatedShows);
                    break;

                case ((int)ContextMenuItem.Trailers):
                    GUICommon.ShowTVShowTrailersMenu(selectedShow);
                    break;

                case ((int)ContextMenuItem.Shouts):
                    GUIShouts.ShoutType = GUIShouts.ShoutTypeEnum.show;
                    GUIShouts.ShowInfo = new ShowShout { IMDbId = selectedShow.Imdb, TVDbId = selectedShow.Tvdb, Title = selectedShow.Title };
                    GUIShouts.Fanart = selectedShow.Images.FanartImageFilename;
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Shouts);
                    break;

                case ((int)ContextMenuItem.Rate):
                    GUICommon.RateShow(selectedShow);
                    OnShowSelected(selectedItem, Facade);
                    selectedShow.Images.NotifyPropertyChanged("PosterImageFilename");
                    break;

                case ((int)ContextMenuItem.ChangeLayout):
                    ShowLayoutMenu();
                    break;

                case ((int)ContextMenuItem.SearchWithMpNZB):
                    string loadingParam = string.Format("search:{0}", selectedShow.Title);
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MpNZB, loadingParam);
                    break;

                case ((int)ContextMenuItem.SearchTorrent):
                    string loadPar = selectedShow.Title;
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MyTorrents, loadPar);
                    break;

                default:
                    break;
            }

            base.OnShowContextMenu();
        }

        #endregion

        #region Private Methods

        private void CheckAndPlayEpisode(bool jumpTo)
        {
            GUIListItem selectedItem = this.Facade.SelectedListItem;
            if (selectedItem == null) return;

            TraktTrendingShow selectedShow = (TraktTrendingShow)selectedItem.TVTag;
            GUICommon.CheckAndPlayFirstUnwatched(Convert.ToInt32(selectedShow.Tvdb), string.IsNullOrEmpty(selectedShow.Imdb) ? selectedShow.Title : selectedShow.Imdb, jumpTo);
        }

        private TraktShowSync CreateSyncData(TraktTrendingShow show)
        {
            if (show == null) return null;

            List<TraktShowSync.Show> shows = new List<TraktShowSync.Show>();

            TraktShowSync.Show syncShow = new TraktShowSync.Show
            {
                TVDBID = show.Tvdb,
                Title = show.Title,
                Year = show.Year
            };
            shows.Add(syncShow);

            TraktShowSync syncData = new TraktShowSync
            {
                UserName = TraktSettings.Username,
                Password = TraktSettings.Password,
                Shows = shows
            };

            return syncData;
        }

        private void AddShowToWatchList(TraktTrendingShow show)
        {
            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncShowWatchList(CreateSyncData(obj as TraktTrendingShow), TraktSyncModes.watchlist);
            })
            {
                IsBackground = true,
                Name = "Adding Show to Watch List"
            };

            syncThread.Start(show);
        }

        private void RemoveShowFromWatchList(TraktTrendingShow show)
        {
            Thread syncThread = new Thread(delegate(object obj)
            {
                TraktAPI.TraktAPI.SyncShowWatchList(CreateSyncData(obj as TraktTrendingShow), TraktSyncModes.unwatchlist);
            })
            {
                IsBackground = true,
                Name = "Removing Show from Watch List"
            };

            syncThread.Start(show);
        }

        private void ShowLayoutMenu()
        {
            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            dlg.Reset();
            dlg.SetHeading(GetLayoutTranslation(CurrentLayout));

            foreach (Layout layout in Enum.GetValues(typeof(Layout)))
            {
                string menuItem = GetLayoutTranslation(layout);
                GUIListItem pItem = new GUIListItem(menuItem);
                if (layout == CurrentLayout) pItem.Selected = true;
                dlg.Add(pItem);
            }

            dlg.DoModal(GUIWindowManager.ActiveWindow);

            if (dlg.SelectedLabel >= 0)
            {
                CurrentLayout = (Layout)dlg.SelectedLabel;
                Facade.SetCurrentLayout(Enum.GetName(typeof(Layout), CurrentLayout));
                GUIControl.SetControlLabel(GetID, layoutButton.GetID, GetLayoutTranslation(CurrentLayout));
            }
        }

        private string GetLayoutTranslation(Layout layout)
        {
            string strLine = string.Empty;
            switch (layout)
            {
                case Layout.List:
                    strLine = GUILocalizeStrings.Get(101);
                    break;
                case Layout.SmallIcons:
                    strLine = GUILocalizeStrings.Get(100);
                    break;
                case Layout.LargeIcons:
                    strLine = GUILocalizeStrings.Get(417);
                    break;
                case Layout.Filmstrip:
                    strLine = GUILocalizeStrings.Get(733);
                    break;
            }
            return strLine;
        }

        private void LoadTrendingShows()
        {
            GUIUtils.SetProperty("#Trakt.Items", string.Empty);

            GUIBackgroundTask.Instance.ExecuteInBackgroundAndCallback(() =>
            {
                return TrendingShows;
            },
            delegate(bool success, object result)
            {
                if (success)
                {
                    IEnumerable<TraktTrendingShow> shows = result as IEnumerable<TraktTrendingShow>;
                    SendTrendingShowsToFacade(shows);
                }
            }, Translation.GettingTrendingShows, true);
        }

        private void SendTrendingShowsToFacade(IEnumerable<TraktTrendingShow> shows)
        {
            // clear facade
            GUIControl.ClearControl(GetID, Facade.GetID);

            if (shows.Count() == 0)
            {
                GUIUtils.ShowNotifyDialog(GUIUtils.PluginName(), Translation.NoTrendingShows);
                GUIWindowManager.ShowPreviousWindow();
                return;
            }

            // sort shows
            var showList = shows.ToList();
            showList.Sort(new GUIListItemShowSorter(TraktSettings.SortByTrendingShows.Field, TraktSettings.SortByTrendingShows.Direction));

            int itemId = 0;
            List<TraktShow.ShowImages> showImages = new List<TraktShow.ShowImages>();

            foreach (var show in showList)
            {
                GUITraktTrendingShowListItem item = new GUITraktTrendingShowListItem(show.Title);

                item.Label2 = show.Year.ToString();
                item.TVTag = show;
                item.Item = show.Images;                
                item.ItemId = Int32.MaxValue - itemId;                
                item.IconImage = "defaultVideo.png";
                item.IconImageBig = "defaultVideoBig.png";
                item.ThumbnailImage = "defaultVideoBig.png";
                item.OnItemSelected += OnShowSelected;
                Utils.SetDefaultIcons(item);
                Facade.Add(item);
                itemId++;

                // add image for download
                showImages.Add(show.Images);
            }

            // Set Facade Layout
            Facade.SetCurrentLayout(Enum.GetName(typeof(Layout), CurrentLayout));
            GUIControl.FocusControl(GetID, Facade.GetID);

            Facade.SelectIndex(PreviousSelectedIndex);

            // set facade properties
            GUIUtils.SetProperty("#itemcount", shows.Count().ToString());
            GUIUtils.SetProperty("#Trakt.Items", string.Format("{0} {1}", shows.Count().ToString(), shows.Count() > 1 ? Translation.SeriesPlural : Translation.Series));
            GUIUtils.SetProperty("#Trakt.Trending.PeopleCount", shows.Sum(s => s.Watchers).ToString());
            GUIUtils.SetProperty("#Trakt.Trending.Description", string.Format(Translation.TrendingTVShowsPeople, shows.Sum(s => s.Watchers).ToString(), shows.Count().ToString()));

            // Download show images Async and set to facade
            GetImages(showImages);
        }

        private void InitProperties()
        {
            // Fanart
            backdrop.GUIImageOne = FanartBackground;
            backdrop.GUIImageTwo = FanartBackground2;
            backdrop.LoadingImage = loadingImage;            

            // load last layout
            CurrentLayout = (Layout)TraktSettings.TrendingShowsDefaultLayout;

            // Update Button States
            UpdateButtonState();

            if (sortButton != null)
            {
                sortButton.SortChanged += (o, e) =>
                {
                    TraktSettings.SortByTrendingShows.Direction = (SortingDirections)(e.Order - 1);
                    PreviousSelectedIndex = 0;
                    UpdateButtonState();
                    LoadTrendingShows();
                };
            }
        }

        private void UpdateButtonState()
        {
            // update layout button label
            GUIControl.SetControlLabel(GetID, layoutButton.GetID, GetLayoutTranslation(CurrentLayout));

            // update sortby button label
            if (sortButton != null)
            {
                sortButton.Label = GUICommon.GetSortByString(TraktSettings.SortByTrendingShows);
                sortButton.IsAscending = (TraktSettings.SortByTrendingShows.Direction == SortingDirections.Ascending);
            }
            GUIUtils.SetProperty("#Trakt.SortBy", GUICommon.GetSortByString(TraktSettings.SortByTrendingShows));
        }

        private void ClearProperties()
        {
            GUIUtils.SetProperty("#Trakt.Trending.PeopleCount", string.Empty);
            GUIUtils.SetProperty("#Trakt.Trending.Description", string.Empty);

            GUIUtils.SetProperty("#Trakt.Show.Watchers", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Watchers.Extra", string.Empty);
            GUICommon.ClearShowProperties(); ;
        }

        private void PublishShowSkinProperties(TraktTrendingShow show)
        {
            GUICommon.SetProperty("#Trakt.Show.Watchers", show.Watchers.ToString());
            GUICommon.SetProperty("#Trakt.Show.Watchers.Extra", show.Watchers > 1 ? string.Format(Translation.PeopleWatching, show.Watchers) : Translation.PersonWatching);
            GUICommon.SetShowProperties(show);
        }

        private void OnShowSelected(GUIListItem item, GUIControl parent)
        {
            TraktTrendingShow show = item.TVTag as TraktTrendingShow;
            PublishShowSkinProperties(show);
            GUIImageHandler.LoadFanart(backdrop, show.Images.FanartImageFilename);
        }

        private void GetImages(List<TraktShow.ShowImages> itemsWithThumbs)
        {
            StopDownload = false;

            // split the downloads in 5+ groups and do multithreaded downloading
            int groupSize = (int)Math.Max(1, Math.Floor((double)itemsWithThumbs.Count / 5));
            int groups = (int)Math.Ceiling((double)itemsWithThumbs.Count() / groupSize);

            for (int i = 0; i < groups; i++)
            {
                List<TraktShow.ShowImages> groupList = new List<TraktShow.ShowImages>();
                for (int j = groupSize * i; j < groupSize * i + (groupSize * (i + 1) > itemsWithThumbs.Count ? itemsWithThumbs.Count - groupSize * i : groupSize); j++)
                {
                    groupList.Add(itemsWithThumbs[j]);
                }

                // sort images so that images that already exist are displayed first
                groupList.Sort((s1, s2) =>
                {
                    int x = Convert.ToInt32(File.Exists(s1.PosterImageFilename)) + Convert.ToInt32(File.Exists(s1.FanartImageFilename));
                    int y = Convert.ToInt32(File.Exists(s2.PosterImageFilename)) + Convert.ToInt32(File.Exists(s2.FanartImageFilename));
                    return y.CompareTo(x);
                });

                new Thread(delegate(object o)
                {
                    List<TraktShow.ShowImages> items = (List<TraktShow.ShowImages>)o;
                    foreach (TraktShow.ShowImages item in items)
                    {
                        #region Poster
                        // stop download if we have exited window
                        if (StopDownload) break;

                        string remoteThumb = item.Poster;
                        string localThumb = item.PosterImageFilename;

                        if (!string.IsNullOrEmpty(remoteThumb) && !string.IsNullOrEmpty(localThumb))
                        {
                            if (GUIImageHandler.DownloadImage(remoteThumb, localThumb))
                            {
                                // notify that image has been downloaded
                                item.NotifyPropertyChanged("PosterImageFilename");
                            }
                        }
                        #endregion

                        #region Fanart
                        // stop download if we have exited window
                        if (StopDownload) break;
                        if (!TraktSettings.DownloadFanart) continue;

                        string remoteFanart = item.Fanart;
                        string localFanart = item.FanartImageFilename;

                        if (!string.IsNullOrEmpty(remoteFanart) && !string.IsNullOrEmpty(localFanart))
                        {
                            if (GUIImageHandler.DownloadImage(remoteFanart, localFanart))
                            {
                                // notify that image has been downloaded
                                item.NotifyPropertyChanged("FanartImageFilename");
                            }
                        }
                        #endregion
                    }
                })
                {
                    IsBackground = true,
                    Name = "Trakt Show Image Downloader " + i.ToString()
                }.Start(groupList);
            }
        }

        #endregion
    }

    public class GUITraktTrendingShowListItem : GUIListItem
    {
        public GUITraktTrendingShowListItem(string strLabel) : base(strLabel) { }

        public object Item
        {
            get { return _Item; }
            set
            {
                _Item = value;
                INotifyPropertyChanged notifier = value as INotifyPropertyChanged;
                if (notifier != null) notifier.PropertyChanged += (s, e) =>
                {
                    if (s is TraktShow.ShowImages && e.PropertyName == "PosterImageFilename")
                        SetImageToGui((s as TraktShow.ShowImages).PosterImageFilename);
                    if (s is TraktShow.ShowImages && e.PropertyName == "FanartImageFilename")
                        UpdateCurrentSelection();

                };
            }
        } protected object _Item;

        /// <summary>
        /// Loads an Image from memory into a facade item
        /// </summary>
        /// <param name="imageFilePath">Filename of image</param>
        protected void SetImageToGui(string imageFilePath)
        {
            if (string.IsNullOrEmpty(imageFilePath)) return;

            // determine the overlays to add to poster
            TraktTrendingShow show = TVTag as TraktTrendingShow;
            MainOverlayImage mainOverlay = MainOverlayImage.None;

            if (show.InWatchList)
                mainOverlay = MainOverlayImage.Watchlist;
            //else if (show.Watched)
            //    mainOverlay = MainOverlayImage.Seenit;

            RatingOverlayImage ratingOverlay = GUIImageHandler.GetRatingOverlay(show.RatingAdvanced);

            // get a reference to a MediaPortal Texture Identifier
            string suffix = Enum.GetName(typeof(MainOverlayImage), mainOverlay) + Enum.GetName(typeof(RatingOverlayImage), ratingOverlay);
            string texture = GUIImageHandler.GetTextureIdentFromFile(imageFilePath, suffix);

            // build memory image
            Image memoryImage = null;
            if (mainOverlay != MainOverlayImage.None || ratingOverlay != RatingOverlayImage.None)
            {
                memoryImage = GUIImageHandler.DrawOverlayOnPoster(imageFilePath, mainOverlay, ratingOverlay);
                if (memoryImage == null) return;

                // load texture into facade item
                if (GUITextureManager.LoadFromMemory(memoryImage, texture, 0, 0, 0) > 0)
                {
                    ThumbnailImage = texture;
                    IconImage = texture;
                    IconImageBig = texture;
                }
            }
            else
            {
                ThumbnailImage = imageFilePath;
                IconImage = imageFilePath;
                IconImageBig = imageFilePath;
            }

            // if selected and is current window force an update of thumbnail
            UpdateCurrentSelection();
        }

        protected void UpdateCurrentSelection()
        {
            GUITrendingShows window = GUIWindowManager.GetWindow(GUIWindowManager.ActiveWindow) as GUITrendingShows;
            if (window != null)
            {
                GUIListItem selectedItem = GUIControl.GetSelectedListItem(87265, 50);
                if (selectedItem == this)
                {
                    GUIWindowManager.SendThreadMessage(new GUIMessage(GUIMessage.MessageType.GUI_MSG_ITEM_SELECT, GUIWindowManager.ActiveWindow, 0, 50, ItemId, 0, null));
                }
            }
        }
    }
}
