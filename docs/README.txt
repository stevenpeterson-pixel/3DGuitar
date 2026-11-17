Concert Acoustic - web version (iPad and other browsers)
=========================================================

This folder is a complete static website. It must be opened through a web address, not by tapping
index.html in the Files app (the audio engine and offline mode need https:// or localhost).

Route 1 - host it once, then play offline
  1. Upload everything in this folder to any static web host that serves https
     (for example GitHub Pages: create a repository, upload these files, turn on Pages in Settings).
  2. On the iPad, open the address in Safari.
  3. Tap Share, then "Add to Home Screen".
  4. Open it from the Home Screen icon. After the first visit it works with no internet.

Route 2 - thumb drive, no internet
  1. Copy this folder to a thumb drive, plug it into the iPad, and copy it into the Files app.
  2. Install an app that can serve a folder to Safari from the iPad itself (a "local web server" app).
  3. Point it at this folder, start it, and open the localhost address it shows in Safari.
  The server app has to be running each time you play.

Playing on a touch screen
  - Fretboard: hold frets with one hand (several fingers at once), strum over the sound hole with the other.
  - Palm mute: rest a finger on the strip by the bridge.
  - Zoom: pinch with two fingers on the wood or the background (not on the strings), or double-tap there
    to switch between the playing view and the whole guitar. Zoom buttons are also under View.
  - Lyrics: the Lyrics button opens a panel on the right; paste the words there and they are remembered.
  - Sound starts after the first tap. If you hear nothing, check the volume and Control Center's mute.

Updating: after changing the Windows source, run build-web.ps1 again and re-upload this folder.
