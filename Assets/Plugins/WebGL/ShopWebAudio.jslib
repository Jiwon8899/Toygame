mergeInto(LibraryManager.library, {
  ShopResumeWebAudio: function () {
    if (typeof WEBAudio !== 'undefined' && WEBAudio.audioContext &&
        WEBAudio.audioContext.state === 'suspended') {
      WEBAudio.audioContext.resume();
    }
  }
});
