# Additive customization

Everything in this folder is compiled automatically by the SDK-style project. No
official FufuLauncher source file, project file, XAML file, or resource file needs
to be edited.

The extension performs three tasks at runtime:

1. Hides the original floating notification center and injects a persistent card
   above the home-page content, limited to the left half of the page.
2. Adds a button that checks in the active mainland-China miHoYo account for both
   Genshin Impact and Zenless Zone Zero.
3. Runs the same combined check-in once on the first home-page visit of each local
   calendar day.

`Update-AdditiveCustom.ps1` merges `upstream/master`, builds the app and native
launcher components, recreates the desktop shortcut, pushes `additive-custom` to
the fork, and optionally starts the app.
