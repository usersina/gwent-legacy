// The page around the game: reloaded for an update (Gwent.Updates.UpdateInstaller), which loads the newest build
mergeInto(LibraryManager.library, {
  GwentReload: function () {
    window.location.reload();
  }
});
