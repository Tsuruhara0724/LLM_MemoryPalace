Place one image per target word in this folder.

Naming convention:
- zapato.png
- aeropuerto.png
- cartera.png

The runtime loads images with Resources.Load<Texture2D>("WordImages/{word}").
If a word image is missing, the runtime falls back to _placeholder.png.
