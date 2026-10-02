// Smoke drifting off the far chimneys (Horizon): particle puffs facing the camera, each turned by its
// own angle (as Godot's particle billboards), a lumpy painted puff (luminance shades it, alpha shapes
// it) in the smoke's colour, faded in and out over its life by the particle colour's alpha. Unshaded
// and unfogged: its colour is already the haze's view of it.
shader_type spatial;
render_mode blend_mix, unshaded, fog_disabled, depth_draw_never, cull_disabled, shadows_disabled;

uniform sampler2D puff : filter_linear_mipmap, repeat_disable;
uniform vec3 color : source_color = vec3(0.8);
uniform float opacity = 0.45;

void vertex() {
	mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	world = world * mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec4 p = texture(puff, UV);
	ALBEDO = color * p.r;
	ALPHA = p.a * COLOR.a * opacity;
}
