// The see-through parts of the first-person marker (the loader's smoke-tinted shell): projected like
// viewmodel.gdshader, with its own FOV and squashed depth, but blended over what's behind it.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 albedo : source_color = vec4(0.25, 0.27, 0.29, 0.5);
uniform float roughness : hint_range(0.0, 1.0) = 0.15;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float viewmodel_fov_deg = 62.0;
uniform float depth_squash : hint_range(0.5, 0.999) = 0.9;

// Paint that has spattered the marker and the gloves, in the first-person rig's frame (game/player/ViewModel.cs).
#include "res://shaders/paint.gdshaderinc"

uniform mat4 paint_frame = mat4(1.0);
varying vec3 paint_vertex;
varying vec3 paint_normal_rig;

void vertex() {
	mat4 projection = PROJECTION_MATRIX;
	float focal = 1.0 / tan(radians(viewmodel_fov_deg) * 0.5);
	float aspect = abs(projection[1][1] / projection[0][0]);
	projection[0][0] = sign(projection[0][0]) * focal / aspect;
	projection[1][1] = sign(projection[1][1]) * focal;
	POSITION = projection * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	POSITION.z = POSITION.w * depth_squash + (1.0 - depth_squash) * POSITION.z;
	// The paint's frame: the first-person rig's (paint_frame takes the world into it).
	paint_vertex = (paint_frame * MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	paint_normal_rig = mat3(paint_frame) * (mat3(MODEL_MATRIX) * NORMAL);
}

void fragment() {
	vec3 colour = albedo.rgb;
	float rough = roughness;
	vec3 normal = NORMAL;
	float painted = splat_apply(paint_vertex, paint_normal_rig, VERTEX, colour, rough, normal);
	NORMAL = normal;
	ALBEDO = colour;
	ALPHA = max(albedo.a, painted);
	ROUGHNESS = rough;
	METALLIC = metallic * (1.0 - painted);
}
