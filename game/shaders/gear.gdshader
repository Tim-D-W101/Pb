// A gear item on a player (marker, loader, tank, mask; game/player/GearModels.cs): each surface of an item's mesh is
// one finish (anodised, plastic, rubber, steel...) painted by one of the wearer's three colours, or by its own. The mesh
// and these materials are shared by everyone wearing the item; the colours are each wearer's own, set per instance. A
// knurl, a weave or a twill is pressed into the surface where the finish has one, and a brand's mark is cut out of the
// marks' atlas.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// The wearer's colours for this item (linear), and their paint for the balls in a see-through loader.
instance uniform vec3 main_colour = vec3(0.2);
instance uniform vec3 second_colour = vec3(0.2);
instance uniform vec3 accent_colour = vec3(0.6);
instance uniform vec3 paint_colour = vec3(0.8, 0.1, 0.1);

// Which colour paints this surface: 0 main, 1 second, 2 accent, 3 its own, 4 the paint.
uniform int paint_slot = 0;
uniform vec3 own_colour : source_color = vec3(0.1);
uniform float roughness : hint_range(0.0, 1.0) = 0.5;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float specular_level : hint_range(0.0, 1.0) = 0.5;
// 0 none, 1 knurl, 2 weave, 3 twill (gear_patterns.gdshaderinc).
uniform int pattern = 0;
// A mark: the atlas's coverage cuts it out.
uniform bool cutout = false;
uniform sampler2D mark_tex : hint_default_white, filter_linear_mipmap_anisotropic;

#include "res://shaders/gear_patterns.gdshaderinc"
// Paint that has hit this item, in its model space (a wearer's painted item has materials of its own).
#include "res://shaders/paint.gdshaderinc"

varying vec3 model_vertex;
varying vec3 model_normal;

void vertex() {
	model_vertex = VERTEX;
	model_normal = NORMAL;
}

vec3 colour() {
	if (paint_slot == 0) {
		return main_colour;
	} else if (paint_slot == 1) {
		return second_colour;
	} else if (paint_slot == 2) {
		return accent_colour;
	} else if (paint_slot == 4) {
		return paint_colour;
	}
	return own_colour;
}

void fragment() {
	vec4 mark = cutout ? texture(mark_tex, UV) : vec4(1.0);
	if (mark.a < 0.5) {
		discard;
	}

	vec3 albedo = colour() * mark.rgb;
	float rough = roughness;
	vec3 normal = NORMAL;
	gear_apply_pattern(pattern, UV, VERTEX, albedo, rough, normal);
	float painted = splat_apply(model_vertex, model_normal, VERTEX, albedo, rough, normal);
	NORMAL = normal;
	ALBEDO = albedo;
	ROUGHNESS = rough;
	METALLIC = metallic * (1.0 - painted);
	SPECULAR = mix(specular_level, 0.5, painted);
}
