// Drops falling from the edges of holes in the roof (RoofDrips): a thin streak facing the camera round
// the vertical, stretched as it falls, pale and see-through, unlit (a drop catches the light from all
// round). Each drop's instance places its middle; a scale of zero hides it.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded, world_vertex_coords;

uniform float width = 0.006;
uniform float streak = 0.15;
uniform vec4 tint : source_color = vec4(0.75, 0.79, 0.84, 0.4);

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float shown = length(MODEL_MATRIX[0].xyz);
	vec3 to_camera = CAMERA_POSITION_WORLD - origin;
	vec3 across = normalize(cross(vec3(0.0, 1.0, 0.0), vec3(to_camera.x, 0.0, to_camera.z) + vec3(1e-4, 0.0, 0.0)));
	vec2 corner = UV - 0.5;
	VERTEX = origin + (across * corner.x * width + vec3(0.0, -corner.y * streak, 0.0)) * shown;
}

void fragment() {
	// Soft at the sides, a rounder head at the bottom.
	float side = 1.0 - smoothstep(0.25, 0.5, abs(UV.x - 0.5));
	float along = smoothstep(0.0, 0.6, UV.y);
	ALBEDO = tint.rgb;
	ALPHA = tint.a * side * along;
}
