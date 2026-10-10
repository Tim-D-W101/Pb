// A see-through loader shell on a player (game/player/GearModels.cs): tinted by one of the wearer's colours and
// blended over the paint inside. Shared by everyone wearing the item; the colours are each wearer's own.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

instance uniform vec3 main_colour = vec3(0.2);
instance uniform vec3 second_colour = vec3(0.2);
instance uniform vec3 accent_colour = vec3(0.6);

// Which colour tints this surface: 0 main, 1 second, 2 accent.
uniform int paint_slot = 0;
uniform float opacity : hint_range(0.0, 1.0) = 0.62;
uniform float roughness : hint_range(0.0, 1.0) = 0.1;

// Paint that has hit the shell, in its model space; paint on it isn't see-through.
#include "res://shaders/paint.gdshaderinc"

varying vec3 model_vertex;
varying vec3 model_normal;

void vertex() {
	model_vertex = VERTEX;
	model_normal = NORMAL;
}

void fragment() {
	vec3 tint = paint_slot == 0 ? main_colour : paint_slot == 1 ? second_colour : accent_colour;
	float rough = roughness;
	vec3 normal = NORMAL;
	float painted = splat_apply(model_vertex, model_normal, VERTEX, tint, rough, normal);
	NORMAL = normal;
	ALBEDO = tint;
	ALPHA = max(opacity, painted);
	ROUGHNESS = rough;
	SPECULAR = 0.6;
}
