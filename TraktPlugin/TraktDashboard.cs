﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using MediaPortal.Util;
using MediaPortal.GUI.Library;
using TraktPlugin.GUI;
using TraktPlugin.TraktAPI;
using TraktPlugin.TraktAPI.DataStructures;
using TraktPlugin.TraktAPI.Enums;
using TraktPlugin.TraktAPI.Extensions;
using Action = MediaPortal.GUI.Library.Action;

namespace TraktPlugin
{
    public enum ActivityView
    {
        community,
        followers,
        following,
        friends,
        friendsandme
    }

    internal class TraktDashboard
    {
        #region Enums

        #endregion

        #region Private Variables
        
        private long ActivityStartTime = 0;

        private Timer ActivityTimer = null;
        private Timer TrendingMoviesTimer = null;
        private Timer TrendingShowsTimer = null;
        private Timer StatisticsTimer = null;

        bool GetFullActivityLoad = false;
        bool TrendingContextMenuIsActive = false;

        DateTime LastTrendingShowUpdate = DateTime.MinValue;
        DateTime LastTrendingMovieUpdate = DateTime.MinValue;

        TraktActivity.Activity PreviousSelectedActivity = null;

        #endregion

        #region Constructor

        public TraktDashboard() { }

        #endregion

        #region Private Methods

        private DashboardTrendingSettings GetTrendingSettings()
        {
            // skinners should set unique window ids per trending so it doesn't matter if we pick the first
            // the whole point of having a collection is to define unique dashboard settings per window otherwise all windows share the same settings

            if (TraktSkinSettings.DashboardTrendingCollection == null)
                return new DashboardTrendingSettings();

            string windowID = GUIWindowManager.ActiveWindow.ToString();

            var trendingSettings = TraktSkinSettings.DashboardTrendingCollection.FirstOrDefault(d => d.MovieWindows.Contains(windowID) || d.TVShowWindows.Contains(windowID));
            return trendingSettings;
        }

        private int GetMaxTrendingProperties()
        {
            if (TraktSkinSettings.DashboardTrendingCollection == null) return 0;
            return TraktSkinSettings.DashboardTrendingCollection.Select(d => d.FacadeMaxItems).Max();
        }

        private GUIFacadeControl GetFacade(int facadeID)
        {
            int i = 0;
            GUIFacadeControl facade = null;

            // window init message does not work unless overridden from a guiwindow class
            // so we need to be ensured that the window is fully loaded
            // before we can get reference to a skin control
            try
            {
                do
                {
                    // get current window
                    var window = GUIWindowManager.GetWindow(GUIWindowManager.ActiveWindow);

                    // get facade control
                    facade = window.GetControl(facadeID) as GUIFacadeControl;
                    if (facade == null) Thread.Sleep(100);

                    i++;
                }
                while (i < 50 && facade == null);
            }
            catch (Exception ex)
            {
                TraktLogger.Error("MediaPortal failed to get the active control");
                TraktLogger.Error(ex.StackTrace);
            }

            if (facade == null)
            {
                TraktLogger.Debug("Unable to find Facade [id:{0}], check that trakt skin settings are correctly defined!", facadeID.ToString());
            }

            return facade;
        }

        private void GetStatistics()
        {
            Thread.CurrentThread.Name = "DashStats";

            // initial publish from persisted settings
            //TODO
            //if (TraktSettings.LastStatistics != null)
            //{
            //    GUICommon.SetStatisticProperties(TraktSettings.LastStatistics);
            //    TraktSettings.LastStatistics = null;
            //}

            // retrieve statistics from online
            //var userProfile = TraktAPI.TraktAPI.GetUserProfile(TraktSettings.Username);
            //if (userProfile != null)
            //{
            //    GUICommon.SetStatisticProperties(userProfile.Stats);
            //    PreviousStatistics = userProfile.Stats;
            //}
        }

        private void ClearSelectedActivityProperties()
        {
            GUIUtils.SetProperty("#Trakt.Selected.Activity.Type", "none");
            GUIUtils.SetProperty("#Trakt.Selected.Activity.Action", "none");

            GUICommon.ClearUserProperties();
            GUICommon.ClearEpisodeProperties();
            GUICommon.ClearMovieProperties();
            GUICommon.ClearShowProperties();

            GUIUtils.SetProperty("#Trakt.Activity.Count", "0");
            GUIUtils.SetProperty("#Trakt.Activity.Items", string.Format("0 {0}", Translation.Activities));
            GUIUtils.SetProperty("#Trakt.Activity.Description", GetActivityDescription((ActivityView)TraktSettings.ActivityStreamView));
        }

        private string GetActivityDescription(ActivityView activityView)
        {
            string description = string.Empty;

            switch (activityView)
            {
                case ActivityView.community:
                    description = Translation.ActivityCommunityDesc;
                    break;

                case ActivityView.followers:
                    description = Translation.ActivityFollowersDesc;
                    break;

                case ActivityView.following:
                    description = Translation.ActivityFollowingDesc;
                    break;

                case ActivityView.friends:
                    description = Translation.ActivityFriendsDesc;
                    break;

                case ActivityView.friendsandme:
                    description = Translation.ActivityFriendsAndMeDesc;
                    break;
            }

            return description;
        }

        private void LoadActivity()
        {
            Thread.CurrentThread.Name = "DashActivity";

            GUIFacadeControl facade = null;

            // get the facade, may need to wait until
            // window has completely loaded
            if (TraktSkinSettings.DashboardActivityFacadeType.ToLowerInvariant() != "none")
            {
                facade = GetFacade((int)TraktDashboardControls.ActivityFacade);
                if (facade == null) return;

                // we may trigger a re-load by switching from
                // community->friends->community
                lock (this)
                {
                    // load facade if empty and we have activity already
                    // facade is empty on re-load of window
                    if (facade.Count == 0 && PreviousActivity != null && PreviousActivity.Activities != null && PreviousActivity.Activities.Count > 0)
                    {
                        PublishActivityProperties(PreviousActivity);
                        LoadActivityFacade(PreviousActivity, facade);
                    }

                    // get latest activity
                    var activities = GetActivity((ActivityView)TraktSettings.ActivityStreamView);

                    // publish properties
                    PublishActivityProperties(activities);

                    // load activity into list
                    LoadActivityFacade(activities, facade);
                }
            }
            
            // only need to publish properties
            if (facade == null && TraktSkinSettings.DashboardActivityPropertiesMaxItems > 0)
            {
                // get latest activity
                var activities = GetActivity((ActivityView)TraktSettings.ActivityStreamView);
                if (activities == null || activities.Activities == null) return;

                // publish properties
                PublishActivityProperties(activities);
                
                // download images
                var avatarImages = new List<GUITraktImage>();
                foreach (var activity in activities.Activities.Take(TraktSkinSettings.DashboardActivityPropertiesMaxItems))
                {
                    avatarImages.Add(new GUITraktImage { UserImages = activity.User.Images });
                }
                GUIUserListItem.GetImages(avatarImages);
            }
        }

        private void PublishActivityProperties()
        {
            PublishActivityProperties(PreviousActivity);
        }
        private void PublishActivityProperties(TraktActivity activity)
        {
            var activities = activity.Activities;
            if (activities == null) return;

            int maxItems = activities.Count() < TraktSkinSettings.DashboardActivityFacadeMaxItems ? activities.Count() : TraktSkinSettings.DashboardActivityFacadeMaxItems;

            for (int i = 0; i < maxItems; i++)
            {
                GUIUtils.SetProperty(string.Format("#Trakt.Activity.{0}.Action", i), activities[i].Action);
                GUIUtils.SetProperty(string.Format("#Trakt.Activity.{0}.Type", i), activities[i].Type);
                GUIUtils.SetProperty(string.Format("#Trakt.Activity.{0}.ActivityPinIcon", i), GetActivityImage(activities[i]));
                GUIUtils.SetProperty(string.Format("#Trakt.Activity.{0}.ActivityPinIconNoExt", i), GetActivityImage(activities[i]).Replace(".png", string.Empty));
                GUIUtils.SetProperty(string.Format("#Trakt.Activity.{0}.Title", i), GUICommon.GetActivityItemName(activities[i]));
                GUIUtils.SetProperty(string.Format("#Trakt.Activity.{0}.Time", i), activities[i].Timestamp.FromISO8601().ToLocalTime().ToShortTimeString());
                GUIUtils.SetProperty(string.Format("#Trakt.Activity.{0}.Day", i), activities[i].Timestamp.FromISO8601().ToLocalTime().DayOfWeek.ToString().Substring(0, 3));
                GUIUtils.SetProperty(string.Format("#Trakt.Activity.{0}.Shout", i), GetActivityShoutText(activities[i]));
            }
        }

        private void LoadActivityFacade(TraktActivity activities, GUIFacadeControl facade)
        {
            if (TraktSkinSettings.DashBoardActivityWindows == null || !TraktSkinSettings.DashBoardActivityWindows.Contains(GUIWindowManager.ActiveWindow.ToString()))
                return;

            // if no activities report to user
            if (activities == null || activities.Activities == null || activities.Activities.Count == 0)
            {
                facade.Clear();

                GUIListItem item = new GUIListItem(Translation.NoActivities);
                facade.Add(item);
                facade.SetCurrentLayout(TraktSkinSettings.DashboardActivityFacadeType);
                ClearSelectedActivityProperties();
                return;
            }

            // if no new activities then nothing to do
            if (facade.Count > 0)
            {
                var mostRecentActivity = facade[0].TVTag as TraktActivity.Activity;
                if (mostRecentActivity != null)
                {
                    if (mostRecentActivity.Timestamp == activities.Activities.First().Timestamp &&
                        mostRecentActivity.User.Username == activities.Activities.First().User.Username)
                    {
                        return;
                    }
                }
            }

            TraktLogger.Debug("Loading Trakt Activity Facade");

            // stop any existing image downloads
            GUIUserListItem.StopDownload = true;

            // clear facade
            GUIControl.ClearControl(GUIWindowManager.ActiveWindow, facade.GetID);

            int itemId = 0;
            int PreviousSelectedIdx = -1;
            var userImages = new List<GUITraktImage>();

            // Add each activity item to the facade
            foreach (var activity in activities.Activities.Distinct().OrderByDescending(a => a.Timestamp))
            {
                if (PreviousSelectedIdx == -1 && PreviousSelectedActivity != null && TraktSettings.RememberLastSelectedActivity)
                {
                    if (activity.Equals(PreviousSelectedActivity))
                        PreviousSelectedIdx = itemId;
                }

                var item = new GUIUserListItem(GUICommon.GetActivityListItemTitle(activity), GUIWindowManager.ActiveWindow);

                string activityImage = GetActivityImage(activity);
                string avatarImage = GetAvatarImage(activity);

                // add image to download
                var images = new GUITraktImage { UserImages = activity.User.Images };
                if (avatarImage == "defaultTraktUser.png")
                    userImages.Add(images);
                    
                item.Label2 = activity.Timestamp.FromISO8601().ToLocalTime().ToShortTimeString();
                item.TVTag = activity;
                item.User = activity.User;
                item.Images = images;
                item.ItemId = Int32.MaxValue - itemId;
                item.IconImage = avatarImage;
                item.IconImageBig = avatarImage;
                item.ThumbnailImage = avatarImage;
                item.PinImage = activityImage;
                item.OnItemSelected += OnActivitySelected;
                facade.Add(item);
                itemId++;
            }

            // Set Facade Layout
            facade.SetCurrentLayout(TraktSkinSettings.DashboardActivityFacadeType);
            facade.SetVisibleFromSkinCondition();

            // Select previously selected item
            if (facade.LayoutControl.IsFocused && PreviousSelectedIdx >= 0)
                facade.SelectIndex(PreviousSelectedIdx);

            // set facade properties
            GUIUtils.SetProperty("#Trakt.Activity.Count", activities.Activities.Count().ToString());
            GUIUtils.SetProperty("#Trakt.Activity.Items", string.Format("{0} {1}", activities.Activities.Count().ToString(), activities.Activities.Count() > 1 ? Translation.Activities : Translation.Activity));
            GUIUtils.SetProperty("#Trakt.Activity.Description", GetActivityDescription((ActivityView)TraktSettings.ActivityStreamView));

            // Download avatar images Async and set to facade
            GUIUserListItem.StopDownload = false;
            GUIUserListItem.GetImages(userImages);

            TraktLogger.Debug("Finished Loading Activity facade");
        }

        /// <summary>
        /// Skinners can use this property to toggle visibility of trending facades/properties
        /// </summary>
        private void SetTrendingVisibility()
        {
            var window = GUIWindowManager.GetWindow(GUIWindowManager.ActiveWindow);
            var toggleButton = window.GetControl((int)TraktDashboardControls.ToggleTrendingCheckButton) as GUICheckButton;

            // if skin does not have checkmark control to toggle trending then exit
            if (toggleButton == null) return;

            if (toggleButton != null)
            {
                var trendingShowsFacade = GetFacade((int)TraktDashboardControls.TrendingShowsFacade);
                var trendingMoviesFacade = GetFacade((int)TraktDashboardControls.TrendingMoviesFacade);

                bool moviesVisible = TraktSettings.DashboardMovieTrendingActive;

                toggleButton.Selected = moviesVisible;
                GUIUtils.SetProperty("#Trakt.Dashboard.TrendingType.Active", moviesVisible ? "movies" : "shows");

                if (trendingMoviesFacade != null)
                {
                    trendingMoviesFacade.Visible = moviesVisible;
                    if (trendingMoviesFacade.FilmstripLayout != null) trendingMoviesFacade.FilmstripLayout.Visible = moviesVisible;
                    if (trendingMoviesFacade.ListLayout != null) trendingMoviesFacade.ListLayout.Visible = moviesVisible;
                    if (trendingMoviesFacade.AlbumListLayout != null) trendingMoviesFacade.AlbumListLayout.Visible = moviesVisible;
                    if (trendingMoviesFacade.ThumbnailLayout != null) trendingMoviesFacade.ThumbnailLayout.Visible = moviesVisible;
                    if (trendingMoviesFacade.CoverFlowLayout != null) trendingMoviesFacade.CoverFlowLayout.Visible = moviesVisible;
                }

                if (trendingShowsFacade != null)
                {
                    trendingShowsFacade.Visible = !moviesVisible;
                    if (trendingShowsFacade.FilmstripLayout != null) trendingShowsFacade.FilmstripLayout.Visible = !moviesVisible;
                    if (trendingShowsFacade.ListLayout != null) trendingShowsFacade.ListLayout.Visible = !moviesVisible;
                    if (trendingShowsFacade.AlbumListLayout != null) trendingShowsFacade.AlbumListLayout.Visible = !moviesVisible;
                    if (trendingShowsFacade.ThumbnailLayout != null) trendingShowsFacade.ThumbnailLayout.Visible = !moviesVisible;
                    if (trendingShowsFacade.CoverFlowLayout != null) trendingShowsFacade.CoverFlowLayout.Visible = !moviesVisible;
                }
            }
        }

        private void LoadTrendingMovies()
        {
            LoadTrendingMovies(false);
        }
        private void LoadTrendingMovies(bool forceReload)
        {
            if (Thread.CurrentThread.Name == null)
                Thread.CurrentThread.Name = "DashMovies";

            GUIFacadeControl facade = null;
            bool isCached;

            var trendingSettings = GetTrendingSettings();
            if (trendingSettings == null) return;

            if (trendingSettings.FacadeType.ToLowerInvariant() != "none")
            {
                // update toggle visibility
                SetTrendingVisibility();

                facade = GetFacade((int)TraktDashboardControls.TrendingMoviesFacade);
                if (facade == null) return;

                // load facade if empty and we have trending already
                // facade is empty on re-load of window
                if (facade.Count == 0 && PreviousTrendingMovies != null && PreviousTrendingMovies.Count() > 0)
                {
                    PublishMovieProperties(PreviousTrendingMovies);
                    LoadTrendingMoviesFacade(PreviousTrendingMovies, facade);
                }

                // get latest trending
                var trendingMovies = GetTrendingMovies(out isCached);

                // prevent an unnecessary reload
                if (!isCached || forceReload)
                {
                    // publish properties
                    PublishMovieProperties(trendingMovies);

                    // load trending into list
                    LoadTrendingMoviesFacade(trendingMovies, facade);
                }
            }
            
            // only publish skin properties
            if (facade == null && trendingSettings.PropertiesMaxItems > 0)
            {
                // get latest trending
                var trendingMovies = GetTrendingMovies(out isCached);

                if (!isCached)
                {
                    if (trendingMovies == null || trendingMovies.Count() == 0) return;

                    // publish properties
                    PublishMovieProperties(trendingMovies);

                    // download images
                    var movieImages = new List<GUITraktImage>();
                    foreach (var trendingItem in trendingMovies)
                    {
                        movieImages.Add(new GUITraktImage { MovieImages = trendingItem.Movie.Images });
                    }
                    GUIMovieListItem.GetImages(movieImages);
                }
            }
        }

        private void PublishMovieProperties()
        {
            PublishMovieProperties(PreviousTrendingMovies);
        }
        private void PublishMovieProperties(IEnumerable<TraktMovieTrending> trendingItems)
        {
            if (trendingItems == null) return;

            if (TraktSettings.FilterTrendingOnDashboard)
                trendingItems = GUICommon.FilterTrendingMovies(trendingItems);

            var trendingList = trendingItems.ToList();
            int maxItems = trendingItems.Count() < GetMaxTrendingProperties() ? trendingItems.Count() : GetMaxTrendingProperties();

            for (int i = 0; i < maxItems; i++)
            {
                var trendingItem = trendingList[i];
                if (trendingItem == null) continue;

                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Watchers", i), trendingItem.Watchers.ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Watchers.Extra", i), trendingItem.Watchers > 1 ? string.Format(Translation.PeopleWatching, trendingItem.Watchers) : Translation.PersonWatching);

                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Id", i), trendingItem.Movie.Ids.Id);
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.TmdbId", i), trendingItem.Movie.Ids.TmdbId);
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.ImdbId", i), trendingItem.Movie.Ids.ImdbId);
                //TODOGUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Certification", i), trendingItem.Certification);
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Overview", i), string.IsNullOrEmpty(trendingItem.Movie.Overview) ? Translation.NoMovieSummary : trendingItem.Movie.Overview);
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Released", i), trendingItem.Movie.Released.FromISO8601().ToShortDateString());
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Runtime", i), trendingItem.Movie.Runtime.ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Tagline", i), trendingItem.Movie.Tagline);
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Title", i), trendingItem.Movie.Title);
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Trailer", i), trendingItem.Movie.Trailer);
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Url", i), string.Format("http://trakt.tv/movies/{0}", trendingItem.Movie.Ids.Slug));
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Year", i), trendingItem.Movie.Year.ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Genres", i), string.Join(", ", trendingItem.Movie.Genres.ToArray()));
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.PosterImageFilename", i), trendingItem.Movie.Images.Poster.LocalImageFilename(ArtworkType.MoviePoster));
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.FanartImageFilename", i), trendingItem.Movie.Images.Fanart.LocalImageFilename(ArtworkType.MovieFanart));
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.InCollection", i), trendingItem.Movie.IsCollected().ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.InWatchList", i), trendingItem.Movie.IsWatchlisted().ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Plays", i), trendingItem.Movie.Plays());
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Watched", i), trendingItem.Movie.IsWatched().ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Rating", i), trendingItem.Movie.UserRating());
                //TODO
                //GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.Icon", i), (trendingItem.Movie.Ratings.LovedCount > trendingItem.Ratings.HatedCount) ? "love" : "hate");
                //GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.HatedCount", i), trendingItem.Movie.Ratings.HatedCount.ToString());
                //GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.LovedCount", i), trendingItem.Movie.Ratings.LovedCount.ToString());
                //GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.Percentage", i), trendingItem.Movie.Ratings.Percentage.ToString());
                //GUICommon.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.Votes", i), trendingItem.Movie.Ratings.Votes.ToString());
            }
        }

        private void ClearMovieProperties()
        {
            for (int i = 0; i < GetMaxTrendingProperties(); i++)
            {
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Watchers", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Watchers.Extra", i), string.Empty);

                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Id", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.ImdbId", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.TmdbId", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Certification", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Overview", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Released", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Runtime", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Tagline", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Title", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Trailer", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Url", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Year", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Genres", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.PosterImageFilename", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.FanartImageFilename", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.InCollection", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.InWatchList", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Plays", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Watched", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Rating", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.RatingAdvanced", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.Icon", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.HatedCount", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.LovedCount", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.Percentage", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Movie.{0}.Ratings.Votes", i), string.Empty);
            }
        }

        private void LoadTrendingMoviesFacade(IEnumerable<TraktMovieTrending> trendingItems, GUIFacadeControl facade)
        {
            if (TraktSkinSettings.DashboardTrendingCollection == null || !TraktSkinSettings.DashboardTrendingCollection.Exists(d => d.MovieWindows.Contains(GUIWindowManager.ActiveWindow.ToString())))
                return;
            
            // get trending settings for window
            var trendingSettings = GetTrendingSettings();
            if (trendingSettings == null) return;

            TraktLogger.Debug("Loading Trakt Trending Movies facade");

            // if no trending, then nothing to do
            if (trendingItems == null || trendingItems.Count() == 0)
                return;

            // stop any existing image downloads
            GUIMovieListItem.StopDownload = true;

            // clear facade
            GUIControl.ClearControl(GUIWindowManager.ActiveWindow, facade.GetID);

            int itemId = 0;
            var movieImages = new List<GUITraktImage>();

            // filter movies
            if (TraktSettings.FilterTrendingOnDashboard)
                trendingItems = GUICommon.FilterTrendingMovies(trendingItems);

            // Add each activity item to the facade
            foreach (var trendingItem in trendingItems.Take(trendingSettings.FacadeMaxItems))
            {
                // add image for download
                var images = new GUITraktImage { MovieImages = trendingItem.Movie.Images };
                movieImages.Add(images);

                var item = new GUIMovieListItem(trendingItem.Movie.Title, GUIWindowManager.ActiveWindow);

                item.Label2 = trendingItem.Movie.Year.ToString();
                item.TVTag = trendingItem;
                item.Movie = trendingItem.Movie;
                item.Images = images;
                item.ItemId = Int32.MaxValue - itemId;
                item.IconImage = GUIImageHandler.GetDefaultPoster(false);
                item.IconImageBig = GUIImageHandler.GetDefaultPoster();
                item.ThumbnailImage = GUIImageHandler.GetDefaultPoster();
                item.OnItemSelected += OnTrendingMovieSelected;
                try
                {
                    facade.Add(item);
                }
                catch { }
                itemId++;
            }

            // Set Facade Layout
            facade.SetCurrentLayout(trendingSettings.FacadeType);
            facade.SetVisibleFromSkinCondition();

            // set facade properties
            GUIUtils.SetProperty("#Trakt.Trending.Movies.Items", string.Format("{0} {1}", trendingItems.Count().ToString(), trendingItems.Count() > 1 ? Translation.Movies : Translation.Movie));
            GUIUtils.SetProperty("#Trakt.Trending.Movies.PeopleCount", trendingItems.Sum(t => t.Watchers).ToString());
            GUIUtils.SetProperty("#Trakt.Trending.Movies.Description", string.Format(Translation.TrendingTVShowsPeople, trendingItems.Sum(t => t.Watchers).ToString(), trendingItems.Count().ToString()));

            // Download images Async and set to facade
            GUIMovieListItem.StopDownload = false;
            GUIMovieListItem.GetImages(movieImages);

            SetTrendingVisibility();

            TraktLogger.Debug("Finished Loading Trending Movies facade");
        }

        private void LoadTrendingShows()
        {
            LoadTrendingShows(false);
        }
        private void LoadTrendingShows(bool forceReload)
        {
            if (Thread.CurrentThread.Name == null)
                Thread.CurrentThread.Name = "DashShows";

            GUIFacadeControl facade = null;
            bool isCached;

            var trendingSettings = GetTrendingSettings();
            if (trendingSettings == null) return;

            if (trendingSettings.FacadeType.ToLowerInvariant() != "none")
            {
                // update toggle visibility
                SetTrendingVisibility();

                facade = GetFacade((int)TraktDashboardControls.TrendingShowsFacade);
                if (facade == null) return;

                // load facade if empty and we have trending already
                // facade is empty on re-load of window
                if (facade.Count == 0 && PreviousTrendingShows != null && PreviousTrendingShows.Count() > 0)
                {
                    PublishShowProperties(PreviousTrendingShows);
                    LoadTrendingShowsFacade(PreviousTrendingShows, facade);
                }

                // get latest trending
                var trendingShows = GetTrendingShows(out isCached);

                // prevent an unnecessary reload
                if (!isCached || forceReload)
                {
                    // publish properties
                    PublishShowProperties(trendingShows);

                    // load trending into list
                    LoadTrendingShowsFacade(trendingShows, facade);
                }
            }
            
            // only publish skin properties
            if (facade == null && GetMaxTrendingProperties() > 0)
            {
                // get latest trending
                var trendingShows = GetTrendingShows(out isCached);
                
                if (!isCached)
                {
                    if (trendingShows == null || trendingShows.Count() == 0) return;

                    // publish properties
                    PublishShowProperties(trendingShows);

                    // download images
                    var showImages = new List<GUITraktImage>();
                    foreach (var trendingItem in trendingShows)
                    {
                        showImages.Add(new GUITraktImage { ShowImages = trendingItem.Show.Images });
                    }
                    GUIShowListItem.GetImages(showImages);
                }
            }
        }

        private void PublishShowProperties()
        {
            PublishShowProperties(PreviousTrendingShows);
        }
        private void PublishShowProperties(IEnumerable<TraktShowTrending> trendingItems)
        {
            if (trendingItems == null) return;

            if (TraktSettings.FilterTrendingOnDashboard)
                trendingItems = GUICommon.FilterTrendingShows(trendingItems);

            var trendingList = trendingItems.ToList();
            int maxItems = trendingItems.Count() < GetMaxTrendingProperties() ? trendingItems.Count() : GetMaxTrendingProperties();

            for (int i = 0; i < maxItems; i++)
            {
                var trendingItem = trendingList[i];
                if (trendingItem == null) continue;

                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Watchers", i), trendingItem.Watchers.ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Watchers.Extra", i), trendingItem.Watchers > 1 ? string.Format(Translation.PeopleWatching, trendingItem.Watchers) : Translation.PersonWatching);

                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Id", i), trendingItem.Show.Ids.ImdbId);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.ImdbId", i), trendingItem.Show.Ids.ImdbId);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.TmdbId", i), trendingItem.Show.Ids.TmdbId);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.TvdbId", i), trendingItem.Show.Ids.TvdbId);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.TvRageId", i), trendingItem.Show.Ids.TvRageId);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Title", i), trendingItem.Show.Title);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Url", i), string.Format("http://trakt.tv/shows/{0}", trendingItem.Show.Ids.Slug));
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.AirDay", i), trendingItem.Show.Airs.Day);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.AirTime", i), trendingItem.Show.Airs.Time);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.AirTimezone", i), trendingItem.Show.Airs.Timezone);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Certification", i), trendingItem.Show.Certification);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Country", i), trendingItem.Show.Country);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.FirstAired", i), trendingItem.Show.FirstAired.FromISO8601().ToShortDateString());
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Network", i), trendingItem.Show.Network);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Overview", i), string.IsNullOrEmpty(trendingItem.Show.Overview) ? Translation.NoShowSummary : trendingItem.Show.Overview);
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Runtime", i), trendingItem.Show.Runtime.ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Year", i), trendingItem.Show.Year.ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Genres", i), string.Join(", ", trendingItem.Show.Genres.ToArray()));
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.InWatchList", i), trendingItem.Show.IsWatchlisted().ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Watched", i), trendingItem.Show.IsWatched().ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Plays", i), trendingItem.Show.Plays());
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Rating", i), trendingItem.Show.UserRating());
                //TODO
                //GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.Icon", i), (trendingItem.Show.Ratings.LovedCount > trendingItem.Show.Ratings.HatedCount) ? "love" : "hate");
                //GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.HatedCount", i), trendingItem.Show.Ratings.HatedCount.ToString());
                //GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.LovedCount", i), trendingItem.Show.Ratings.LovedCount.ToString());
                //GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.Percentage", i), trendingItem.Show.Ratings.Percentage.ToString());
                //GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.Votes", i), trendingItem.Show.Ratings.Votes.ToString());
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.PosterImageFilename", i), trendingItem.Show.Images.Poster.LocalImageFilename(ArtworkType.ShowPoster));
                GUICommon.SetProperty(string.Format("#Trakt.Show.{0}.FanartImageFilename", i), trendingItem.Show.Images.Fanart.LocalImageFilename(ArtworkType.ShowFanart));
            }
        }

        private void ClearShowProperties()
        {
            for (int i = 0; i < GetMaxTrendingProperties(); i++)
            {
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Watchers", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Watchers.Extra", i), string.Empty);

                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Id", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.ImdbId", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.TvdbId", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.TmdbId", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.TvRageId", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Title", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Url", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.AirDay", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.AirTime", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.AirTimezone", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Certification", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Country", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.FirstAired", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Network", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Overview", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Runtime", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Year", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Genres", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.InWatchList", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Watched", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Plays", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Rating", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.RatingAdvanced", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.Icon", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.HatedCount", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.LovedCount", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.Percentage", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.Ratings.Votes", i), string.Empty);
                GUIUtils.SetProperty(string.Format("#Trakt.Show.{0}.FanartImageFilename", i), string.Empty);
            }
        }

        private void LoadTrendingShowsFacade(IEnumerable<TraktShowTrending> trendingItems, GUIFacadeControl facade)
        {
            if (TraktSkinSettings.DashboardTrendingCollection == null || !TraktSkinSettings.DashboardTrendingCollection.Exists(d => d.MovieWindows.Contains(GUIWindowManager.ActiveWindow.ToString())))
                return;

            // get trending settings
            var trendingSettings = GetTrendingSettings();
            if (trendingSettings == null) return;

            TraktLogger.Debug("Loading Trakt Trending Shows facade");

            // if no trending, then nothing to do
            if (trendingItems == null || trendingItems.Count() == 0)
                return;

            // stop any existing image downloads
            GUIShowListItem.StopDownload = true;

            // clear facade
            GUIControl.ClearControl(GUIWindowManager.ActiveWindow, facade.GetID);

            int itemId = 0;
            var showImages = new List<GUITraktImage>();

            // filter shows
            if (TraktSettings.FilterTrendingOnDashboard)
                trendingItems = GUICommon.FilterTrendingShows(trendingItems);

            // Add each activity item to the facade
            foreach (var trendingItem in trendingItems.Take(trendingSettings.FacadeMaxItems))
            {
                // add image for download
                var images = new GUITraktImage { ShowImages = trendingItem.Show.Images };
                showImages.Add(images);

                var item = new GUIShowListItem(trendingItem.Show.Title, GUIWindowManager.ActiveWindow);

                item.Label2 = trendingItem.Show.Year.ToString();
                item.TVTag = trendingItem;
                item.TVTag = trendingItem.Show;
                item.Images = images;
                item.ItemId = Int32.MaxValue - itemId;
                item.IconImage = GUIImageHandler.GetDefaultPoster(false);
                item.IconImageBig = GUIImageHandler.GetDefaultPoster();
                item.ThumbnailImage = GUIImageHandler.GetDefaultPoster();
                item.OnItemSelected += OnTrendingShowSelected;
                try
                {
                    facade.Add(item);
                }
                catch { }
                itemId++;
            }

            // Set Facade Layout
            facade.SetCurrentLayout(trendingSettings.FacadeType);
            facade.SetVisibleFromSkinCondition();

            // set facade properties
            GUIUtils.SetProperty("#Trakt.Trending.Shows.Items", string.Format("{0} {1}", trendingItems.Count().ToString(), trendingItems.Count() > 1 ? Translation.SeriesPlural : Translation.Series));
            GUIUtils.SetProperty("#Trakt.Trending.Shows.PeopleCount", trendingItems.Sum(t => t.Watchers).ToString());
            GUIUtils.SetProperty("#Trakt.Trending.Shows.Description", string.Format(Translation.TrendingTVShowsPeople, trendingItems.Sum(t => t.Watchers).ToString(), trendingItems.Count().ToString()));

            // Download images Async and set to facade
            GUIShowListItem.StopDownload = false;
            GUIShowListItem.GetImages(showImages);
            
            SetTrendingVisibility();

            TraktLogger.Debug("Finished Loading Trending Shows facade");
        }

        private string GetActivityImage(TraktActivity.Activity activity)
        {
            if (activity == null || string.IsNullOrEmpty(activity.Action))
                return string.Empty;

            string imageFilename = string.Empty;
            ActivityAction action = (ActivityAction)Enum.Parse(typeof(ActivityAction), activity.Action);

            switch (action)
            {
                case ActivityAction.checkin:
                case ActivityAction.watching:
                    imageFilename = "traktActivityWatching.png";
                    break;

                case ActivityAction.seen:
                case ActivityAction.scrobble:
                    imageFilename = "traktActivityWatched.png";
                    break;

                case ActivityAction.collection:
                    imageFilename = "traktActivityCollected.png";
                    break;

                case ActivityAction.rating:
                    imageFilename = int.Parse(activity.RatingAdvanced) > 5 ? "traktActivityLove.png" : "traktActivityHate.png";
                    break;

                case ActivityAction.watchlist:
                    imageFilename = "traktActivityWatchlist.png";
                    break;

                case ActivityAction.shout:
                case ActivityAction.review:
                    imageFilename = "traktActivityShout.png";
                    break;

                case ActivityAction.item_added:
                case ActivityAction.created:
                    imageFilename = "traktActivityList.png";
                    break;
            }

            return imageFilename;
        }

        private string GetAvatarImage(TraktActivity.Activity activity)
        {
            string filename = activity.User.Images.Avatar.LocalImageFilename(ArtworkType.Avatar);
            if (string.IsNullOrEmpty(filename) || !System.IO.File.Exists(filename))
            {
                filename = "defaultTraktUser.png";
            }
            return filename;
        }

        private string GetActivityShoutText(TraktActivity.Activity activity)
        {
            if (activity.Action != ActivityAction.shout.ToString()) return string.Empty;
            if (activity.Shout.Spoiler) return Translation.HiddenToPreventSpoilers;
            return activity.Shout.Text;
        }

        private string GetActivityReviewText(TraktActivity.Activity activity)
        {
            if (activity.Action != ActivityAction.review.ToString()) return string.Empty;
            if (activity.Review.Spoiler) return Translation.HiddenToPreventSpoilers;
            return activity.Review.Text;
        }

        private IEnumerable<TraktMovieTrending> GetTrendingMovies(out bool isCached)
        {
            isCached = false;
            double timeSinceLastUpdate = DateTime.Now.Subtract(LastTrendingMovieUpdate).TotalMilliseconds;

            if (PreviousTrendingMovies == null || TraktSettings.DashboardTrendingPollInterval <= timeSinceLastUpdate)
            {
                TraktLogger.Debug("Getting trending movies from trakt");
                var trendingMovies = TraktAPI.TraktAPI.GetTrendingMovies();
                if (trendingMovies != null && trendingMovies.Count() > 0)
                {
                    LastTrendingMovieUpdate = DateTime.Now;
                    PreviousTrendingMovies = trendingMovies;
                }
            }
            else
            {
                TraktLogger.Debug("Getting trending movies from cache");
                isCached = true;
                // update start interval
                int startInterval = (int)(TraktSettings.DashboardTrendingPollInterval - timeSinceLastUpdate);
                TrendingMoviesTimer.Change(startInterval, TraktSettings.DashboardTrendingPollInterval);
            }
            return PreviousTrendingMovies;
        }

        private IEnumerable<TraktShowTrending> GetTrendingShows(out bool isCached)
        {
            isCached = false;
            double timeSinceLastUpdate = DateTime.Now.Subtract(LastTrendingShowUpdate).TotalMilliseconds;

            if (PreviousTrendingShows == null || TraktSettings.DashboardTrendingPollInterval <= timeSinceLastUpdate)
            {
                TraktLogger.Debug("Getting trending shows from trakt");
                var trendingShows = TraktAPI.TraktAPI.GetTrendingShows();
                if (trendingShows != null && trendingShows.Count() > 0)
                {
                    LastTrendingShowUpdate = DateTime.Now;
                    PreviousTrendingShows = trendingShows;
                }
            }
            else
            {
                TraktLogger.Debug("Getting trending shows from cache");
                isCached = true;
                // update start interval
                int startInterval = (int)(TraktSettings.DashboardTrendingPollInterval - timeSinceLastUpdate);
                TrendingShowsTimer.Change(startInterval, TraktSettings.DashboardTrendingPollInterval);
            }
            return PreviousTrendingShows;
        }

        private TraktActivity GetActivity(ActivityView activityView)
        {
            SetUpdateAnimation(true);

            if (PreviousActivity == null || PreviousActivity.Activities == null || ActivityStartTime <= 0 || GetFullActivityLoad)
            {
                switch (activityView)
                {
                    case ActivityView.community:
                        PreviousActivity = TraktAPI.TraktAPI.GetCommunityActivity();
                        break;

                    case ActivityView.followers:
                        PreviousActivity = TraktAPI.TraktAPI.GetFollowersActivity();
                        break;

                    case ActivityView.following:
                        PreviousActivity = TraktAPI.TraktAPI.GetFollowingActivity();
                        break;

                    case ActivityView.friends:
                        PreviousActivity = TraktAPI.TraktAPI.GetFriendActivity(false);
                        break;

                    case ActivityView.friendsandme:
                        PreviousActivity = TraktAPI.TraktAPI.GetFriendActivity(true);
                        break;
                }
                GetFullActivityLoad = false;
            }
            else
            {
                TraktActivity incrementalActivity = null;

                // get latest incremental change using last current timestamp as start point
                switch (activityView)
                {
                    case ActivityView.community:
                        incrementalActivity = TraktAPI.TraktAPI.GetCommunityActivity(null, null, ActivityStartTime, DateTime.UtcNow.ToEpoch());
                        break;

                    case ActivityView.followers:
                        incrementalActivity = TraktAPI.TraktAPI.GetFollowersActivity(null, null, ActivityStartTime, DateTime.UtcNow.ToEpoch());
                        break;

                    case ActivityView.following:
                        incrementalActivity = TraktAPI.TraktAPI.GetFollowingActivity(null, null, ActivityStartTime, DateTime.UtcNow.ToEpoch());
                        break;

                    case ActivityView.friends:
                        incrementalActivity = TraktAPI.TraktAPI.GetFriendActivity(null, null, ActivityStartTime, DateTime.UtcNow.ToEpoch(), false);
                        break;

                    case ActivityView.friendsandme:
                        incrementalActivity = TraktAPI.TraktAPI.GetFriendActivity(null, null, ActivityStartTime, DateTime.UtcNow.ToEpoch(), true);
                        break;
                }
               
                // join the Previous request with the current
                if (incrementalActivity != null && incrementalActivity.Activities != null)
                {
                    PreviousActivity.Activities = incrementalActivity.Activities.Union(PreviousActivity.Activities).Take(TraktSkinSettings.DashboardActivityFacadeMaxItems).ToList();
                    PreviousActivity.Timestamps = incrementalActivity.Timestamps;
                }
            }

            // store current timestamp and only request incremental change next time
            if (PreviousActivity != null && PreviousActivity.Timestamps != null)
            {
                ActivityStartTime = PreviousActivity.Timestamps.Current;
            }

            SetUpdateAnimation(false);

            return PreviousActivity;
        }

        private bool IsDashBoardWindow()
        {
            bool hasDashBoard = false;

            if (TraktSkinSettings.DashBoardActivityWindows != null && TraktSkinSettings.DashBoardActivityWindows.Contains(GUIWindowManager.ActiveWindow.ToString()))
                hasDashBoard = true;
            if (TraktSkinSettings.DashboardTrendingCollection != null && TraktSkinSettings.DashboardTrendingCollection.Exists(d => d.MovieWindows.Contains(GUIWindowManager.ActiveWindow.ToString())))
                hasDashBoard = true;
            if (TraktSkinSettings.DashboardTrendingCollection != null && TraktSkinSettings.DashboardTrendingCollection.Exists(d=> d.TVShowWindows.Contains(GUIWindowManager.ActiveWindow.ToString())))
                hasDashBoard = true;

            return hasDashBoard;
        }

        private void ShowTrendingShowsContextMenu()
        {
            var trendingShowsFacade = GetFacade((int)TraktDashboardControls.TrendingShowsFacade);
            if (trendingShowsFacade == null) return;

            var dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            if (dlg == null) return;

            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            var selectedItem = trendingShowsFacade.SelectedListItem;
            var selectedTrendingItem = selectedItem.TVTag as TraktShowTrending;

            GUICommon.CreateTrendingShowsContextMenu(ref dlg, selectedTrendingItem.Show, true);

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)TrendingContextMenuItem.AddToWatchList):
                    TraktHelper.AddShowToWatchList(selectedTrendingItem.Show);
                    //TODOselectedTrendingItem.InWatchList = true;
                    OnTrendingShowSelected(selectedItem, trendingShowsFacade);
                    (selectedItem as GUIShowListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.ShowSeasonInfo):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.ShowSeasons, selectedTrendingItem.ToJSON());
                    break;

                case ((int)TrendingContextMenuItem.MarkAsWatched):
                    GUICommon.MarkShowAsWatched(selectedTrendingItem.Show);
                    break;

                case ((int)TrendingContextMenuItem.AddToLibrary):
                    GUICommon.AddShowToCollection(selectedTrendingItem.Show);
                    break;

                case ((int)TrendingContextMenuItem.RemoveFromWatchList):
                    TraktHelper.RemoveShowFromWatchList(selectedTrendingItem.Show);
                    //TODOselectedTrendingItem.InWatchList = false;
                    OnTrendingShowSelected(selectedItem, trendingShowsFacade);
                    (selectedItem as GUIShowListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.AddToList):
                    TraktHelper.AddRemoveShowInUserList(selectedTrendingItem.Show, false);
                    break;

                case ((int)TrendingContextMenuItem.Related):
                    TraktHelper.ShowRelatedShows(selectedTrendingItem.Show);
                    break;

                case ((int)TrendingContextMenuItem.Filters):
                    if (GUICommon.ShowTVShowFiltersMenu())
                        LoadTrendingShows(true);
                    break;

                case ((int)TrendingContextMenuItem.Trailers):
                    GUICommon.ShowTVShowTrailersMenu(selectedTrendingItem.Show);
                    break;

                case ((int)TrendingContextMenuItem.Shouts):
                    TraktHelper.ShowTVShowShouts(selectedTrendingItem.Show);
                    break;

                case ((int)TrendingContextMenuItem.Rate):
                    GUICommon.RateShow(selectedTrendingItem.Show);
                    OnTrendingShowSelected(selectedItem, trendingShowsFacade);
                    (selectedItem as GUIShowListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.SearchWithMpNZB):
                    string loadingParam = string.Format("search:{0}", selectedTrendingItem.Show.Title);
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MpNZB, loadingParam);
                    break;

                case ((int)TrendingContextMenuItem.SearchTorrent):
                    string loadPar = selectedTrendingItem.Show.Title;
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MyTorrents, loadPar);
                    break;

                default:
                    break;
            }
        }

        private void ShowTrendingMoviesContextMenu()
        {
            var trendingMoviesFacade = GetFacade((int)TraktDashboardControls.TrendingMoviesFacade);
            if (trendingMoviesFacade == null) return;

            var dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            if (dlg == null) return;

            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            var selectedItem = trendingMoviesFacade.SelectedListItem;
            var selectedTrendingItem = selectedItem.TVTag as TraktMovieTrending;

            GUICommon.CreateTrendingMoviesContextMenu(ref dlg, selectedTrendingItem.Movie, true);

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)TrendingContextMenuItem.MarkAsWatched):
                    TraktHelper.AddMovieToWatchHistory(selectedTrendingItem.Movie);
                    if (selectedTrendingItem.Movie.Plays() == 0) //TODOselectedTrendingItem.Plays = 1;
                    //TODOselectedTrendingItem.Watched = true;
                    selectedItem.IsPlayed = true;
                    OnTrendingMovieSelected(selectedItem, trendingMoviesFacade);
                    (selectedItem as GUIMovieListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.MarkAsUnWatched):
                    TraktHelper.RemoveMovieFromWatchHistory(selectedTrendingItem.Movie);
                    //TODOselectedTrendingItem.Watched = false;
                    selectedItem.IsPlayed = false;
                    OnTrendingMovieSelected(selectedItem, trendingMoviesFacade);
                    (selectedItem as GUIMovieListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.AddToWatchList):
                    TraktHelper.AddMovieToWatchList(selectedTrendingItem.Movie, true);
                    //TODOselectedTrendingItem.InWatchList = true;
                    OnTrendingMovieSelected(selectedItem, trendingMoviesFacade);
                    (selectedItem as GUIMovieListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.RemoveFromWatchList):
                    TraktHelper.RemoveMovieFromWatchList(selectedTrendingItem.Movie, true);
                    //TODOselectedTrendingItem.InWatchList = false;
                    OnTrendingMovieSelected(selectedItem, trendingMoviesFacade);
                    (selectedItem as GUIMovieListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.AddToList):
                    TraktHelper.AddRemoveMovieInUserList(selectedTrendingItem.Movie, false);
                    break;

                case ((int)TrendingContextMenuItem.Filters):
                    if (GUICommon.ShowMovieFiltersMenu())
                        LoadTrendingMovies(true);
                    break;

                case ((int)TrendingContextMenuItem.AddToLibrary):
                    TraktHelper.AddMovieToCollection(selectedTrendingItem.Movie);
                    //TODOselectedTrendingItem.InCollection = true;
                    OnTrendingMovieSelected(selectedItem, trendingMoviesFacade);
                    (selectedItem as GUIMovieListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.RemoveFromLibrary):
                    TraktHelper.RemoveMovieFromCollection(selectedTrendingItem.Movie);
                    //TODOselectedTrendingItem.InCollection = false;
                    OnTrendingMovieSelected(selectedItem, trendingMoviesFacade);
                    (selectedItem as GUIMovieListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.Related):
                    TraktHelper.ShowRelatedMovies(selectedTrendingItem.Movie);
                    break;

                case ((int)TrendingContextMenuItem.Rate):
                    GUICommon.RateMovie(selectedTrendingItem.Movie);
                    OnTrendingMovieSelected(selectedItem, trendingMoviesFacade);
                    (selectedItem as GUIMovieListItem).Images.NotifyPropertyChanged("Poster");
                    break;

                case ((int)TrendingContextMenuItem.Shouts):
                    TraktHelper.ShowMovieShouts(selectedTrendingItem.Movie);
                    break;

                case ((int)TrendingContextMenuItem.Trailers):
                    GUICommon.ShowMovieTrailersMenu(selectedTrendingItem.Movie);
                    break;

                case ((int)TrendingContextMenuItem.SearchWithMpNZB):
                    string loadingParam = string.Format("search:{0}", selectedTrendingItem.Movie.Title);
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MpNZB, loadingParam);
                    break;

                case ((int)TrendingContextMenuItem.SearchTorrent):
                    string loadPar = selectedTrendingItem.Movie.Title;
                    GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MyTorrents, loadPar);
                    break;

                default:
                    break;
            }
        }

        private bool ShowActivityViewMenu()
        {
            var dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            if (dlg == null) return false;

            dlg.Reset();
            dlg.SetHeading(Translation.View);

            GUIListItem listItem = null;

            listItem = new GUIListItem(Translation.Community);
            dlg.Add(listItem);
            listItem.ItemId = (int)ActivityView.community;

            listItem = new GUIListItem(Translation.Followers);
            dlg.Add(listItem);
            listItem.ItemId = (int)ActivityView.followers;

            listItem = new GUIListItem(Translation.Following);
            dlg.Add(listItem);
            listItem.ItemId = (int)ActivityView.following;

            listItem = new GUIListItem(Translation.Friends);
            dlg.Add(listItem);
            listItem.ItemId = (int)ActivityView.friends;

            listItem = new GUIListItem(Translation.FriendsAndMe);
            dlg.Add(listItem);
            listItem.ItemId = (int)ActivityView.friendsandme;


            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return false;

            TraktSettings.ActivityStreamView = dlg.SelectedId;
            GUIUtils.SetProperty("#Trakt.Activity.Description", GetActivityDescription((ActivityView)TraktSettings.ActivityStreamView));
            return true;
        }

        private void ShowActivityContextMenu()
        {
            var activityFacade = GetFacade((int)TraktDashboardControls.ActivityFacade);
            if (activityFacade == null) return;

            var dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            if (dlg == null) return;

            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            GUIListItem listItem = null;

            // activity view menu
            listItem = new GUIListItem(Translation.ChangeView);
            dlg.Add(listItem);
            listItem.ItemId = (int)ActivityContextMenuItem.ChangeView;
            
            var activity = activityFacade.SelectedListItem.TVTag as TraktActivity.Activity;

            if (activity != null && !string.IsNullOrEmpty(activity.Action) && !string.IsNullOrEmpty(activity.Type))
            {
                // userprofile - only load for unprotected users
                if (!activity.User.IsPrivate)
                {
                    listItem = new GUIListItem(Translation.UserProfile);
                    dlg.Add(listItem);
                    listItem.ItemId = (int)ActivityContextMenuItem.UserProfile;
                }

                if (((ActivityView)TraktSettings.ActivityStreamView == ActivityView.community ||
                     (ActivityView)TraktSettings.ActivityStreamView == ActivityView.followers) && !((activityFacade.SelectedListItem as GUIUserListItem).IsFollowed))
                {
                    // allow user to follow person
                    listItem = new GUIListItem(Translation.Follow);
                    dlg.Add(listItem);
                    listItem.ItemId = (int)ActivityContextMenuItem.FollowUser;
                }

                // if selected activity is an episode or show, add 'Season Info'
                if (activity.Show != null)
                {
                    listItem = new GUIListItem(Translation.ShowSeasonInfo);
                    dlg.Add(listItem);
                    listItem.ItemId = (int)ActivityContextMenuItem.ShowSeasonInfo;
                }

                // get a list of common actions to perform on the selected item
                if (activity.Movie != null || activity.Show != null)
                {
                    var listItems = GUICommon.GetContextMenuItemsForActivity();
                    foreach (var item in listItems)
                    {
                        int itemId = item.ItemId;
                        dlg.Add(item);
                        item.ItemId = itemId;
                    }
                }
            }

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {                
                case ((int)ActivityContextMenuItem.ChangeView):
                    if (ShowActivityViewMenu())
                    {
                        GetFullActivityLoad = true;
                        StartActivityPolling();
                    }
                    else
                    {
                        ShowActivityContextMenu();
                        return;
                    }
                    break;

                case ((int)ActivityContextMenuItem.UserProfile):
                    GUIUserProfile.CurrentUser = activity.User.Username;
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.UserProfile);
                    break;

                case ((int)ActivityContextMenuItem.FollowUser):
                    if (GUIUtils.ShowYesNoDialog(Translation.Network, string.Format(Translation.SendFollowRequest, activity.User.Username), true))
                    {
                        GUINetwork.FollowUser(activity.User);
                        GUINetwork.ClearCache();
                        (activityFacade.SelectedListItem as GUIUserListItem).IsFollowed = true;
                    }
                    break;
                case ((int)ActivityContextMenuItem.ShowSeasonInfo):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.ShowSeasons, activity.Show.ToJSON());
                    break;

                case ((int)ActivityContextMenuItem.AddToList):
                    if (activity.Movie != null)
                        TraktHelper.AddRemoveMovieInUserList(activity.Movie, false);
                    else if (activity.Episode != null)
                        TraktHelper.AddRemoveEpisodeInUserList(activity.Episode, false);
                    else
                        TraktHelper.AddRemoveShowInUserList(activity.Show, false);
                    break;

                case ((int)ActivityContextMenuItem.AddToWatchList):
                    if (activity.Movie != null)
                        TraktHelper.AddMovieToWatchList(activity.Movie, true);
                    else if (activity.Episode != null)
                        TraktHelper.AddEpisodeToWatchList(activity.Episode);
                    else
                        TraktHelper.AddShowToWatchList(activity.Show);
                    break;

                case ((int)ActivityContextMenuItem.Shouts):
                    if (activity.Movie != null)
                        TraktHelper.ShowMovieShouts(activity.Movie);
                    else if (activity.Episode != null)
                        TraktHelper.ShowEpisodeShouts(activity.Show, activity.Episode);
                    else
                        TraktHelper.ShowTVShowShouts(activity.Show);
                    break;

                case ((int)ActivityContextMenuItem.Rate):
                    if (activity.Movie != null)
                        GUICommon.RateMovie(activity.Movie);
                    else if (activity.Episode != null)
                        GUICommon.RateEpisode(activity.Episode);
                    else
                        GUICommon.RateShow(activity.Show);
                    break;

                case ((int)ActivityContextMenuItem.Trailers):
                    if (activity.Movie != null) 
                        GUICommon.ShowMovieTrailersMenu(activity.Movie); 
                    else
                        GUICommon.ShowTVShowTrailersMenu(activity.Show, activity.Episode);
                    break;
            }
        }

        private void SetUpdateAnimation(bool enable)
        {
            // get control
            var window = GUIWindowManager.GetWindow(GUIWindowManager.ActiveWindow);
            var control = window.GetControl((int)TraktDashboardControls.DashboardAnimation);
            if (control == null) return;

            try
            {
                var animation = control as GUIAnimation;

                if (animation != null)
                {
                    if (enable)
                        animation.AllocResources();
                    else
                        animation.Dispose();

                    animation.Visible = enable;
                }
            }
            catch (Exception) { }
        }

        private void ViewShout(TraktActivity.Activity activity)
        {
            switch (activity.Type)
            {
                case "movie":
                    TraktHelper.ShowMovieShouts(activity.Movie);
                    break;

                case "show":
                    TraktHelper.ShowTVShowShouts(activity.Show);
                    break;

                case "episode":
                    TraktHelper.ShowEpisodeShouts(activity.Show, activity.Episode);
                    break;

                default:
                    break;
            }
        }

        private void PlayActivityItem(bool jumpTo)
        {
            // get control
            var activityFacade = GetFacade((int)TraktDashboardControls.ActivityFacade);
            if (activityFacade == null) return;

            // get selected item in facade
            TraktActivity.Activity activity = activityFacade.SelectedListItem.TVTag as TraktActivity.Activity;

            if (activity == null || string.IsNullOrEmpty(activity.Action) || string.IsNullOrEmpty(activity.Type))
                return;

            ActivityAction action = (ActivityAction)Enum.Parse(typeof(ActivityAction), activity.Action);
            ActivityType type = (ActivityType)Enum.Parse(typeof(ActivityType), activity.Type);

            switch (type)
            {
                case ActivityType.episode:
                    if (action == ActivityAction.seen || action == ActivityAction.collection)
                    {
                        if (activity.Episodes.Count > 1)
                        {
                            GUICommon.CheckAndPlayFirstUnwatchedEpisode(activity.Show, jumpTo);
                            return;
                        }
                    } 
                    GUICommon.CheckAndPlayEpisode(activity.Show, activity.Episode);
                    break;

                case ActivityType.show:
                    GUICommon.CheckAndPlayFirstUnwatchedEpisode(activity.Show, jumpTo);
                    break;

                case ActivityType.movie:
                    GUICommon.CheckAndPlayMovie(jumpTo, activity.Movie);
                    break;

                case ActivityType.list:
                    if (action == ActivityAction.item_added)
                    {
                        // return the name of the item added to the list
                        switch (activity.ListItem.Type)
                        {
                            case "show":
                                GUICommon.CheckAndPlayFirstUnwatchedEpisode(activity.ListItem.Show, jumpTo);
                                break;

                            case "episode":
                                GUICommon.CheckAndPlayEpisode(activity.ListItem.Show, activity.ListItem.Episode);
                                break;

                            case "movie":
                                GUICommon.CheckAndPlayMovie(jumpTo, activity.ListItem.Movie);
                                break;
                        }
                    }
                    break;
            }
        }

        private void PlayShow(bool jumpTo)
        {
            // get control
            var facade = GetFacade((int)TraktDashboardControls.TrendingShowsFacade);
            if (facade == null) return;

            // get selected item in facade
            var trendingItem = facade.SelectedListItem.TVTag as TraktShowTrending;

            GUICommon.CheckAndPlayFirstUnwatchedEpisode(trendingItem.Show, jumpTo);
        }

        private void PlayMovie(bool jumpTo)
        {
            // get control
            var facade = GetFacade((int)TraktDashboardControls.TrendingMoviesFacade);
            if (facade == null) return;

            // get selected item in facade
            var trendingItem = facade.SelectedListItem.TVTag as TraktMovieTrending;

            GUICommon.CheckAndPlayMovie(jumpTo, trendingItem.Movie);
        }        

        #endregion

        #region Public Properties

        public TraktActivity PreviousActivity { get; set; }
        public IEnumerable<TraktMovieTrending> PreviousTrendingMovies { get; set; }
        public IEnumerable<TraktShowTrending> PreviousTrendingShows { get; set; }
        //TODOpublic TraktUserProfile.Statistics PreviousStatistics { get; set; }        

        #endregion

        #region Event Handlers

        private void OnActivitySelected(GUIListItem item, GUIControl parent)
        {
            TraktActivity.Activity activity = item.TVTag as TraktActivity.Activity;
            if (activity == null || string.IsNullOrEmpty(activity.Action) || string.IsNullOrEmpty(activity.Type))
            {
                ClearSelectedActivityProperties();
                return;
            }

            // remember last selected item
            PreviousSelectedActivity = activity;

            // set type and action properties
            GUIUtils.SetProperty("#Trakt.Selected.Activity.Type", activity.Type);
            GUIUtils.SetProperty("#Trakt.Selected.Activity.Action", activity.Action);

            GUICommon.SetUserProperties(activity.User);

            ActivityAction action = (ActivityAction)Enum.Parse(typeof(ActivityAction), activity.Action);
            ActivityType type = (ActivityType)Enum.Parse(typeof(ActivityType), activity.Type);

            switch (type)
            {
                case ActivityType.episode:
                    if (action == ActivityAction.seen || action == ActivityAction.collection)
                    {
                        if (activity.Episodes.Count > 1)
                        {
                            GUICommon.SetEpisodeProperties(activity.Show, activity.Episodes.First());
                        }
                        else
                        {
                            GUICommon.SetEpisodeProperties(activity.Show, activity.Episode);
                        }
                    }
                    else
                    {
                        GUICommon.SetEpisodeProperties(activity.Show, activity.Episode);
                    }
                    GUICommon.SetShowProperties(activity.Show);
                    break;

                case ActivityType.show:
                    GUICommon.SetShowProperties(activity.Show);
                    break;

                case ActivityType.movie:
                    GUICommon.SetMovieProperties(activity.Movie);
                    break;

                case ActivityType.list:
                    if (action == ActivityAction.item_added)
                    {
                        // return the name of the item added to the list
                        switch (activity.ListItem.Type)
                        {
                            case "show":
                                GUICommon.SetShowProperties(activity.ListItem.Show);
                                break;

                            case "episode":
                                GUICommon.SetShowProperties(activity.ListItem.Show);
                                GUICommon.SetEpisodeProperties(activity.Show, activity.ListItem.Episode);
                                break;

                            case "movie":
                                GUICommon.SetMovieProperties(activity.ListItem.Movie);
                                break;
                        }
                    }
                    break;
            }
        }

        private void OnTrendingShowSelected(GUIListItem item, GUIControl parent)
        {
            var trendingItem = item.TVTag as TraktShowTrending;
            if (trendingItem == null)
            {
                GUICommon.ClearShowProperties();
                return;
            }

            GUICommon.SetProperty("#Trakt.Show.Watchers", trendingItem.Watchers.ToString());
            GUICommon.SetProperty("#Trakt.Show.Watchers.Extra", trendingItem.Watchers > 1 ? string.Format(Translation.PeopleWatching, trendingItem.Watchers) : Translation.PersonWatching);
            GUICommon.SetShowProperties(trendingItem.Show);
        }

        private void OnTrendingMovieSelected(GUIListItem item, GUIControl parent)
        {
            var trendingItem = item.TVTag as TraktMovieTrending;
            if (trendingItem == null)
            {
                GUICommon.ClearMovieProperties();
                return;
            }

            GUICommon.SetProperty("#Trakt.Movie.Watchers", trendingItem.Watchers.ToString());
            GUICommon.SetProperty("#Trakt.Movie.Watchers.Extra", trendingItem.Watchers > 1 ? string.Format(Translation.PeopleWatching, trendingItem.Watchers) : Translation.PersonWatching);
            GUICommon.SetMovieProperties(trendingItem.Movie);
        }

        private void GUIWindowManager_Receivers(GUIMessage message)
        {
            if (!IsDashBoardWindow()) return;

            switch (message.Message)
            {                   
                case GUIMessage.MessageType.GUI_MSG_CLICKED:
                    if (message.SenderControlId == (int)TraktDashboardControls.ToggleTrendingCheckButton)
                    {
                        TraktSettings.DashboardMovieTrendingActive = !TraktSettings.DashboardMovieTrendingActive;
                        SetTrendingVisibility();
                    }

                    if (message.Param1 != 7) return; // mouse click, enter key, remote ok, only

                    if (message.SenderControlId == (int)TraktDashboardControls.ActivityFacade)
                    {
                        var activityFacade = GetFacade((int)TraktDashboardControls.ActivityFacade);
                        if (activityFacade == null) return;

                        var activity = activityFacade.SelectedListItem.TVTag as TraktActivity.Activity;
                        if (activity == null || string.IsNullOrEmpty(activity.Action) || string.IsNullOrEmpty(activity.Type))
                            return;

                        ActivityAction action = (ActivityAction)Enum.Parse(typeof(ActivityAction), activity.Action);
                        ActivityType type = (ActivityType)Enum.Parse(typeof(ActivityType), activity.Type);

                        switch (action)
                        {
                            case ActivityAction.review:
                            case ActivityAction.shout:
                                // view shout in shouts window
                                ViewShout(activity);
                                break;

                            case ActivityAction.item_added:
                                // load users list
                                //TODOGUIListItems.CurrentList = new TraktUserList { Slug = activity.List.Slug, Name = activity.List.Name };
                                GUIListItems.CurrentUser = activity.User.Username;
                                GUIWindowManager.ActivateWindow((int)TraktGUIWindows.ListItems);
                                break;

                            case ActivityAction.created:
                                // load users lists
                                GUILists.CurrentUser = activity.User.Username;
                                GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Lists);
                                break;

                            case ActivityAction.watchlist:
                                // load users watchlist
                                if (type == ActivityType.movie)
                                {
                                    GUIWatchListMovies.CurrentUser = activity.User.Username;
                                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.WatchedListMovies);
                                }
                                else if (type == ActivityType.show)
                                {
                                    GUIWatchListShows.CurrentUser = activity.User.Username;
                                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.WatchedListShows);
                                }
                                else
                                {
                                    GUIWatchListEpisodes.CurrentUser = activity.User.Username;
                                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.WatchedListEpisodes);
                                }
                                break;

                            default:
                                PlayActivityItem(true);
                                break;
                        }
                    }
                    if (message.SenderControlId == (int)TraktDashboardControls.TrendingShowsFacade)
                    {
                        if (TraktSettings.EnableJumpToForTVShows)
                        {
                            PlayShow(true);
                        }
                        else
                        {
                            var facade = GetFacade((int)TraktDashboardControls.TrendingShowsFacade);
                            if (facade == null) return;

                            var trendingItem = facade.SelectedListItem.TVTag as TraktShowTrending;

                            GUIWindowManager.ActivateWindow((int)TraktGUIWindows.ShowSeasons, trendingItem.Show.ToJSON());
                        }
                    }
                    if (message.SenderControlId == (int)TraktDashboardControls.TrendingMoviesFacade)
                    {
                        PlayMovie(true);
                    }
                    break;

                case GUIMessage.MessageType.GUI_MSG_WINDOW_INIT:
                    // doesn't work, only if overridden from a guiwindow class
                    break;

                default:
                    break;
            }
        }

        private void GUIWindowManager_OnNewAction(Action action)
        {
            if (!IsDashBoardWindow()) return;

            var activeWindow = GUIWindowManager.GetWindow(GUIWindowManager.ActiveWindow);

            switch (action.wID)
            {
                case Action.ActionType.ACTION_CONTEXT_MENU:
                    if (activeWindow.GetFocusControlId() == (int)TraktDashboardControls.ActivityFacade)
                    {
                        TrendingContextMenuIsActive = true;
                        ShowActivityContextMenu();
                    }
                    else if (activeWindow.GetFocusControlId() == (int)TraktDashboardControls.TrendingMoviesFacade)
                    {
                        TrendingContextMenuIsActive = true;
                        ShowTrendingMoviesContextMenu();
                    }
                    else if (activeWindow.GetFocusControlId() == (int)TraktDashboardControls.TrendingShowsFacade)
                    {
                        TrendingContextMenuIsActive = true;
                        ShowTrendingShowsContextMenu();
                    }
                    TrendingContextMenuIsActive = false;
                    break;

                case Action.ActionType.ACTION_PLAY:
                case Action.ActionType.ACTION_MUSIC_PLAY:
                    if (activeWindow.GetFocusControlId() == (int)TraktDashboardControls.ActivityFacade)
                    {
                        PlayActivityItem(false);
                    }
                    if (activeWindow.GetFocusControlId() == (int)TraktDashboardControls.TrendingShowsFacade)
                    {
                        PlayShow(false);
                    }
                    if (activeWindow.GetFocusControlId() == (int)TraktDashboardControls.TrendingMoviesFacade)
                    {
                        PlayMovie(false);
                    }
                    break;
                
                case Action.ActionType.ACTION_MOVE_DOWN:
                    // handle ondown for filmstrips as mediaportal skin navigation for ondown is broken
                    // issue has been resolved in MP 1.5.0 so only do it for earlier releases
                    if (TraktSettings.MPVersion < new Version(1, 5, 0, 0))
                    {
                        if (!TrendingContextMenuIsActive && activeWindow.GetFocusControlId() == (int)TraktDashboardControls.TrendingShowsFacade)
                        {
                            var control = GetFacade(activeWindow.GetFocusControlId());
                            if (control == null) return;

                            if (control.CurrentLayout != GUIFacadeControl.Layout.Filmstrip) return;

                            // set focus on correct control
                            GUIControl.FocusControl(GUIWindowManager.ActiveWindow, (int)TraktDashboardControls.TrendingMoviesFacade);
                        }
                        else if (!TrendingContextMenuIsActive && activeWindow.GetFocusControlId() == (int)TraktDashboardControls.TrendingMoviesFacade)
                        {
                            var control = GetFacade(activeWindow.GetFocusControlId());
                            if (control == null) return;

                            if (control.CurrentLayout != GUIFacadeControl.Layout.Filmstrip) return;

                            // set focus on correct control
                            GUIControl.FocusControl(GUIWindowManager.ActiveWindow, (int)TraktDashboardControls.ActivityFacade);
                        }
                    }
                    break;

                default:
                    break;
            }
        }
         
        #endregion

        #region Public Methods

        public void Init()
        {
            GUIWindowManager.Receivers += new SendMessageHandler(GUIWindowManager_Receivers);
            GUIWindowManager.OnNewAction +=new OnActionHandler(GUIWindowManager_OnNewAction);

            // Clear Properties
            ClearMovieProperties();
            ClearShowProperties();

            // Load from Persisted Settings
            if (TraktSettings.LastActivityLoad != null && TraktSettings.LastActivityLoad.Activities != null)
            {
                PreviousActivity = TraktSettings.LastActivityLoad;
                if (TraktSettings.LastActivityLoad.Timestamps != null)
                {
                    ActivityStartTime = TraktSettings.LastActivityLoad.Timestamps.Current;
                }
            }
            if (TraktSettings.LastTrendingShows != null)
            {
                PreviousTrendingShows = TraktSettings.LastTrendingShows;
            }
            if (TraktSettings.LastTrendingMovies != null)
            {
                PreviousTrendingMovies = TraktSettings.LastTrendingMovies;
            }

            // initialize timercallbacks
            if (TraktSkinSettings.DashBoardActivityWindows != null && TraktSkinSettings.DashBoardActivityWindows.Count > 0)
            {
                ClearSelectedActivityProperties();
                ActivityTimer = new Timer(new TimerCallback((o) => { LoadActivity(); }), null, Timeout.Infinite, Timeout.Infinite);
            }

            if (TraktSkinSettings.DashboardTrendingCollection != null && TraktSkinSettings.DashboardTrendingCollection.Exists(d => d.MovieWindows.Count > 0))
            {
                TrendingMoviesTimer = new Timer(new TimerCallback((o) => { LoadTrendingMovies(); }), null, Timeout.Infinite, Timeout.Infinite);
            }

            if (TraktSkinSettings.DashboardTrendingCollection != null && TraktSkinSettings.DashboardTrendingCollection.Exists(d => d.TVShowWindows.Count > 0))
            {
                TrendingShowsTimer = new Timer(new TimerCallback((o) => { LoadTrendingShows(); }), null, Timeout.Infinite, Timeout.Infinite);
            }

            if (TraktSkinSettings.HasDashboardStatistics)
            {
                StatisticsTimer = new Timer(new TimerCallback((o) => { GetStatistics(); }), null, 3000, 3600000);
            }
        }

        public void StartTrendingMoviesPolling()
        {
            if (TrendingMoviesTimer != null)
            {
                TrendingMoviesTimer.Change(TraktSettings.DashboardLoadDelay, TraktSettings.DashboardTrendingPollInterval);
            }
        }

        public void StartTrendingShowsPolling()
        {
            if (TrendingShowsTimer != null)
            {
                TrendingShowsTimer.Change(TraktSettings.DashboardLoadDelay, TraktSettings.DashboardTrendingPollInterval);
            }
        }

        public void StartActivityPolling()
        {
            if (ActivityTimer != null)
            {
                ActivityTimer.Change(TraktSettings.DashboardLoadDelay, TraktSettings.DashboardActivityPollInterval);
            }
        }

        public void StopActivityPolling()
        {
            if (ActivityTimer != null)
            {
                ActivityTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        public void StopTrendingMoviesPolling()
        {
            if (TrendingMoviesTimer != null)
            {
                TrendingMoviesTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        public void StopTrendingShowsPolling()
        {
            if (TrendingShowsTimer != null)
            {
                TrendingShowsTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        #endregion
    }
}
