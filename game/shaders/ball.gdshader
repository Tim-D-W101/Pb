// Paintballs (one MultiMesh). Instance transform = position + uniform scale by the true radius,
// INSTANCE_CUSTOM.xyz = world velocity, COLOR = team colour.
// - Minimum on-screen size: far balls are drawn larger so incoming paint stays readable
//   (cosmetic only; collision always uses the true 17.3 mm).
// - Optional motion streak: the trailing half is stretched back along the velocity.
shader_type spatial;
render_mode skip_vertex_transform, cull_back, specular_schlick_ggx;

uniform float min_pixels = 3.0;
uniform float streak_time = 0.004;
uniform float roughness : hint_range(0.0, 1.0) = 0.25;

void vertex() {
	float radius = length(MODEL_MATRIX[0].xyz);
	vec3 center = (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float distance_to_camera = max(-center.z, 0.05);
	float diameter_px = 2.0 * radius * abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y * 0.5 / distance_to_camera;
	float grow = max(1.0, min_pixels / max(diameter_px, 1e-4));

	vec3 offset = mat3(MODELVIEW_MATRIX) * VERTEX * grow;
	vec3 velocity = mat3(VIEW_MATRIX) * INSTANCE_CUSTOM.xyz;
	float speed = length(velocity);
	if (speed > 0.01 && streak_time > 0.0) {
		vec3 direction = velocity / speed;
		float trailing = step(0.0, -dot(offset, direction));
		offset -= direction * speed * streak_time * trailing;
	}

	VERTEX = center + offset;
	NORMAL = normalize(mat3(MODELVIEW_MATRIX) * NORMAL);
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = roughness;
	SPECULAR = 0.6;
}
