shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Everything is in weathered.gdshaderinc, shared with weathered_edge.gdshader (the rolling ground's cover edges).
#include "res://shaders/weathered.gdshaderinc"
