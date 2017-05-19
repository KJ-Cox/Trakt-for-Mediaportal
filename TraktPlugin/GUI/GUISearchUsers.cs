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
using MediaPortal.GUI.Video;
using MediaPortal.Util;
using MediaPortal.Video.Database;
using TraktAPI;
using TraktAPI.DataStructures;
using Action = MediaPortal.GUI.Library.Action;

namespace TraktPlugin.GUI
{
    public class GUISearchUsers : GUIWindow
    {
        #region Skin Controls

        [SkinControl(2)]
        protected GUIButtonControl layoutButton = null;

        [SkinControl(50)]
        protected GUIFacadeControl Facade = null;

        #endregion

        #region Enums

        enum ContextMenuItem
        {
            FollowUser,
            UserProfile,
            ChangeLayout
        }

        #endregion

        #region Constructor

        public GUISearchUsers()
        {

        }

        #endregion

        #region Public Variables

        public static string SearchTerm { get; set; }
        public static IEnumerable<TraktUserSummary> Users { get; set; }

        #endregion

        #region Private Variables

        bool StopDownload { get; set; }
        bool SearchTermChanged { get; set; }
        string PreviousSearchTerm { get; set; }
        Layout CurrentLayout { get; set; }
        int PreviousSelectedIndex = 0;

        #endregion

        #region Base Overrides

        public override int GetID
        {
            get
            {
                return (int)TraktGUIWindows.SearchUsers;
            }
        }

        public override bool Init()
        {
            return Load(GUIGraphicsContext.Skin + @"\Trakt.Search.Users.xml");
        }

        protected override void OnPageLoad()
        {
            base.OnPageLoad();

            if (string.IsNullOrEmpty(_loadParameter) && Users == null)
            {
                GUIWindowManager.ActivateWindow(GUIWindowManager.GetPreviousActiveWindow());
                return;
            }

            // Clear GUI Properties
            ClearProperties();

            // Init Properties
            InitProperties();

            // Load Search Results
            LoadSearchResults();
        }

        protected override void OnPageDestroy(int new_windowId)
        {
            StopDownload = true;
            ClearProperties();

            _loadParameter = null;

            // save settings
            TraktSettings.SearchUsersDefaultLayout = (int)CurrentLayout;

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
                        var selectedUser = Facade.SelectedListItem.TVTag as TraktUserSummary;
                        if (selectedUser == null) break;

                        if (!selectedUser.IsPrivate)
                        {
                            GUIUserProfile.CurrentUser = selectedUser.Username;
                            GUIWindowManager.ActivateWindow((int)TraktGUIWindows.UserProfile);
                        }
                        else
                        {
                            if (GUIUtils.ShowYesNoDialog(Translation.Network, string.Format(Translation.SendFollowRequest, selectedUser.Username), true))
                            {
                                GUINetwork.FollowUser(selectedUser);
                                GUINetwork.ClearCache();
                            }
                        }
                    }
                    break;

                // Layout Button
                case (2):
                    CurrentLayout = GUICommon.ShowLayoutMenu(CurrentLayout, PreviousSelectedIndex);
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
                    base.OnAction(action);
                    break;

                case Action.ActionType.ACTION_PREVIOUS_MENU:
                    // clear search criteria if going back
                    SearchTerm = string.Empty;
                    Users = null;
                    base.OnAction(action);
                    break;

                default:
                    base.OnAction(action);
                    break;
            }
        }

        protected override void OnShowContextMenu()
        {
            if (GUIBackgroundTask.Instance.IsBusy) return;

            var selectedItem = this.Facade.SelectedListItem;
            if (selectedItem == null) return;

            var selectedUser = selectedItem.TVTag as TraktUserSummary;
            if (selectedUser == null) return;

            var dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            if (dlg == null) return;

            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            GUIListItem listItem = null;

            // Follow User
            // Only show menu item if user has an account as this is an unprotected area.
            if (!string.IsNullOrEmpty(TraktSettings.Username) && !string.IsNullOrEmpty(TraktSettings.Password) && !((selectedItem as GUIUserListItem).IsFollowed))
            {
                listItem = new GUIListItem(Translation.FollowUser);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.FollowUser;
            }

            // User Profile
            if (!selectedUser.IsPrivate)
            {
                listItem = new GUIListItem(Translation.UserProfile);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.UserProfile;
            }

            // Change Layout
            listItem = new GUIListItem(Translation.ChangeLayout);
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.ChangeLayout;

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)ContextMenuItem.FollowUser):
                    if (GUIUtils.ShowYesNoDialog(Translation.Network, string.Format(Translation.SendFollowRequest, selectedItem.Label), true))
                    {
                        GUINetwork.FollowUser(selectedUser);
                        GUINetwork.ClearCache();
                        (selectedItem as GUIUserListItem).IsFollowed = true;
                    }
                    break;

                case ((int)ContextMenuItem.UserProfile):
                    GUIUserProfile.CurrentUser = selectedUser.Username;
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.UserProfile);
                    break;

                case ((int)ContextMenuItem.ChangeLayout):
                    CurrentLayout = GUICommon.ShowLayoutMenu(CurrentLayout, PreviousSelectedIndex);
                    break;
            }

            base.OnShowContextMenu();
        }

        #endregion

        #region Private Methods

        private void LoadSearchResults()
        {
            GUIUtils.SetProperty("#Trakt.Items", string.Empty);

            GUIBackgroundTask.Instance.ExecuteInBackgroundAndCallback(() =>
            {
                // People can be null if invoking search from loading parameters
                // Internally we set the People to load
                if (Users == null && !string.IsNullOrEmpty(SearchTerm))
                {
                    // search online
                    var searchResults = TraktAPI.TraktAPI.SearchForUsers(SearchTerm);
                    if (searchResults != null)
                    {
                        Users = searchResults.Select(s => s.User);
                    }
                }
                return Users;
            },
            delegate(bool success, object result)
            {
                if (success)
                {
                    var users = result as IEnumerable<TraktUserSummary>;
                    SendSearchResultsToFacade(users);
                }
            }, Translation.GettingSearchResults, true);
        }

        private void SendSearchResultsToFacade(IEnumerable<TraktUserSummary> users)
        {
            // clear facade
            GUIControl.ClearControl(GetID, Facade.GetID);

            if (users == null)
            {
                GUIUtils.ShowNotifyDialog(Translation.Error, Translation.ErrorGeneral);
                GUIWindowManager.ShowPreviousWindow();
                Users = null;
                return;
            }

            if (users.Count() == 0)
            {
                GUIUtils.ShowNotifyDialog(GUIUtils.PluginName(), Translation.NoSearchResultsFound);
                GUIWindowManager.ShowPreviousWindow();
                Users = null;
                return;
            }

            int itemId = 0;
            var userImages = new List<GUITraktImage>();

            // Add each user
            foreach (var user in users)
            {
                // add image to download
                var images = new GUITraktImage { UserImages = user.Images };
                userImages.Add(images);

                var item = new GUIUserListItem(user.Username, (int)TraktGUIWindows.SearchUsers);

                item.Images = images;
                item.TVTag = user;
                item.ItemId = Int32.MaxValue - itemId;
                item.IconImage = "defaultTraktUser.png";
                item.IconImageBig = "defaultTraktUserBig.png";
                item.ThumbnailImage = "defaultTraktUserBig.png";
                item.OnItemSelected += OnUserSelected;
                Utils.SetDefaultIcons(item);
                Facade.Add(item);
                itemId++;
            }

            // Set Facade Layout
            Facade.SetCurrentLayout(Enum.GetName(typeof(Layout), CurrentLayout));
            GUIControl.FocusControl(GetID, Facade.GetID);

            if (SearchTermChanged) PreviousSelectedIndex = 0;
            Facade.SelectIndex(PreviousSelectedIndex);

            // set facade properties
            GUIUtils.SetProperty("#itemcount", users.Count().ToString());
            GUIUtils.SetProperty("#Trakt.Items", string.Format("{0} {1}", users.Count().ToString(), users.Count() > 1 ? Translation.Users : Translation.User));

            // Download images Async and set to facade
            GUIUserListItem.GetImages(userImages);
        }

        private void InitProperties()
        {
            // set search term from loading parameter
            if (!string.IsNullOrEmpty(_loadParameter))
            {
                TraktLogger.Debug("User Search Loading Parameter: {0}", _loadParameter);
                SearchTerm = _loadParameter;
            }

            // remember previous search term
            SearchTermChanged = false;
            if (PreviousSearchTerm != SearchTerm) SearchTermChanged = true;
            PreviousSearchTerm = SearchTerm;

            // set context property
            GUIUtils.SetProperty("#Trakt.Search.SearchTerm", SearchTerm);
            
            // load last layout
            CurrentLayout = (Layout)TraktSettings.SearchUsersDefaultLayout;

            // update button label
            if (layoutButton != null)
                GUIControl.SetControlLabel(GetID, layoutButton.GetID, GUICommon.GetLayoutTranslation(CurrentLayout));
        }

        private void ClearProperties()
        {
            GUIUtils.SetProperty("#Trakt.Search.SearchTerm", string.Empty);
            GUICommon.ClearUserProperties();
        }

        private void PublishSkinProperties(TraktUserSummary user)
        {
            GUICommon.SetUserProperties(user);
        }

        private void OnUserSelected(GUIListItem item, GUIControl parent)
        {
            PreviousSelectedIndex = Facade.SelectedListItemIndex;

            var user = item.TVTag as TraktUserSummary;
            PublishSkinProperties(user);
        }
        #endregion
    }
}