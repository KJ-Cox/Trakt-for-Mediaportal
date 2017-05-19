﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using MediaPortal.GUI.Library;
using TraktPlugin.Extensions;
using TraktAPI.DataStructures;

namespace TraktPlugin.GUI
{
    public class GUIUserListItem : GUIListItem
    {
        /// <summary>
        /// The id of the window that contains the gui list items (facade)
        /// </summary>
        private int WindowID { get; set; }

        public GUIUserListItem(string strLabel, int windowID) : base(strLabel.RemapHighOrderChars())
        {
            this.WindowID = windowID;
        }
                
        public bool IsFriend { get; set; }
        public bool IsFollower { get; set; }
        public bool IsFollowed { get; set; }
        public bool IsFollowerRequest { get; set; }
        public bool IsShout { get; set; }

        public TraktUserSummary User { get; set; }

        /// <summary>
        /// Images attached to a gui list item
        /// </summary>
        public GUITraktImage Images
        {
            get { return _Images; }
            set
            {
                _Images = value;
                var notifier = value as INotifyPropertyChanged;
                if (notifier != null) notifier.PropertyChanged += (s, e) =>
                {
                    if (s is GUITraktImage && e.PropertyName == "Avatar")
                        SetImageToGui((s as GUITraktImage).UserImages.Avatar.LocalImageFilename(ArtworkType.Avatar));
                };
            }
        }
        protected GUITraktImage _Images;

        /// <summary>
        /// Set this to true to stop downloading any images
        /// e.g. when exiting the window
        /// </summary>
        internal static bool StopDownload { get; set; }

        /// <summary>
        /// Download all images attached to the GUI List Control
        /// TODO: Make part of a GUI Base Window
        /// </summary>
        /// <param name="itemsWithThumbs">List of images to get</param>
        internal static void GetImages(List<GUITraktImage> itemsWithThumbs)
        {
            StopDownload = false;

            // split the downloads in 5+ groups and do multithreaded downloading
            int groupSize = (int)Math.Max(1, Math.Floor((double)itemsWithThumbs.Count / 5));
            int groups = (int)Math.Ceiling((double)itemsWithThumbs.Count() / groupSize);

            for (int i = 0; i < groups; i++)
            {
                var groupList = new List<GUITraktImage>();
                for (int j = groupSize * i; j < groupSize * i + (groupSize * (i + 1) > itemsWithThumbs.Count ? itemsWithThumbs.Count - groupSize * i : groupSize); j++)
                {
                    groupList.Add(itemsWithThumbs[j]);
                }
                
                new Thread(delegate(object o)
                {
                    var items = (List<GUITraktImage>)o;
                    foreach (var item in items)
                    {
                        #region Avatar
                        if (item.UserImages != null && item.UserImages.Avatar != null)
                        {
                            // stop download if we have exited window
                            if (StopDownload) break;

                            string remoteThumb = item.UserImages.Avatar.FullSize;
                            string localThumb = item.UserImages.Avatar.LocalImageFilename(ArtworkType.Avatar);

                            if (!string.IsNullOrEmpty(remoteThumb) && !string.IsNullOrEmpty(localThumb))
                            {
                                if (GUIImageHandler.DownloadImage(remoteThumb, localThumb))
                                {
                                    if (StopDownload) break;

                                    // notify that image has been downloaded
                                    item.NotifyPropertyChanged("Avatar");
                                }
                            }
                        }
                        #endregion
                    }
                })
                {
                    IsBackground = true,
                    Name = "ImageDownloader" + i.ToString()
                }.Start(groupList);
            }
        }

        /// <summary>
        /// Loads an Image from memory into a facade item
        /// </summary>
        /// <param name="imageFilePath">Filename of image</param>
        protected void SetImageToGui(string imageFilePath)
        {
            if (string.IsNullOrEmpty(imageFilePath)) return;

            // check if this user item is a shout
            // we may need to apply a rating overlay to the avatar
            if (TVTag is TraktComment)
            {
                var shout = TVTag as TraktComment;

                // add a rating overlay if user has rated item
                var ratingOverlay = GUIImageHandler.GetRatingOverlay(shout.UserRating);

                // get a reference to a MediaPortal Texture Identifier
                string suffix = Enum.GetName(typeof(RatingOverlayImage), ratingOverlay);
                string texture = GUIImageHandler.GetTextureIdentFromFile(imageFilePath, suffix);

                // build memory image, resize avatar as they come in different sizes sometimes
                Image memoryImage = null;
                if (ratingOverlay != RatingOverlayImage.None)
                {
                    memoryImage = GUIImageHandler.DrawOverlayOnAvatar(imageFilePath, ratingOverlay, new Size(140, 140));
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
            }
            else
            {
                ThumbnailImage = imageFilePath;
                IconImage = imageFilePath;
                IconImageBig = imageFilePath;
            }

            // if selected and is current window force an update of thumbnail
            this.UpdateItemIfSelected(WindowID, ItemId);
        }
    }
}
