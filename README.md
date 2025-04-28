# GayZoe-zoerecolor

Functionally this mod uses a compute shader to selectively determine a variety of colors on zoes main texture like her red suit or blue scarf and then lerps that color with a user selected tint color with a varying T value in order to maintain zoes gradients
The c# side of this is mostly managing the color edit menu, loading presets, and driving the compute shader variables when settings changed.
