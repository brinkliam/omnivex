// Omnivex: device class for the WebGL player. iPadOS Safari reports itself as a Mac, so a Mac platform with a
// multi-touch screen is treated as a tablet. The page template sets window.omxDeviceClass first; this mirrors it.
mergeInto(LibraryManager.library, {
  OMX_IsTouchDevice: function () {
    if (typeof window !== "undefined" && window.omxDeviceClass) return window.omxDeviceClass === "mobile" ? 1 : 0;
    var ua = navigator.userAgent || "";
    var touchMac = /Macintosh|MacIntel/.test(ua + " " + (navigator.platform || "")) && navigator.maxTouchPoints > 1;
    return (/iPhone|iPad|iPod|Android/i.test(ua) || touchMac) ? 1 : 0;
  }
});
