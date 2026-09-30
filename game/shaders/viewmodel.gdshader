// First-person marker: drawn with its own vertical FOV (independent of the player's FOV setting)
// and with depth squashed into the nearest slice of the (reversed-Z) depth range, so it always
// draws in front of the world and never clips into a bunker the player is hugging.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform vec4 albedo : source_color = vec4(0.18, 0.18, 0.2, 1.0);
uniform float roughness : hint_range(0.0, 1.0) = 0.5;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float viewmodel_fov_deg = 62.0;
// Fraction of the depth range left to the world; the marker lives in the rest (nearest).
uniform float depth_squash : hint_range(0.5, 0.999) = 0.9;

void vertex() {
	mat4 projection = PROJECTION_MATRIX;
	float focal = 1.0 / tan(radians(viewmodel_fov_deg) * 0.5);
	float aspect = abs(projection[1][1] / projection[0][0]);
	projection[0][0] = sign(projection[0][0]) * focal / aspect;
	projection[1][1] = sign(projection[1][1]) * focal;
	POSITION = projection * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	// Reversed-Z: NDC depth 1 is the near plane. Remap [0, 1] → [depth_squash, 1].
	POSITION.z = POSITION.w * depth_squash + (1.0 - depth_squash) * POSITION.z;
}

void fragment() {
	ALBEDO = albedo.rgb;
	ROUGHNESS = roughness;
	METALLIC = metallic;
}
