shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// The kit's surfaces (weathered.gdshaderinc) with ragged edges, for the rolling ground's cover where it meets other ground
// (M5.8): a variant of its own, so the discard costs nothing on every other wall and prop.
#define EDGE_DITHER
#include "res://shaders/weathered.gdshaderinc"
