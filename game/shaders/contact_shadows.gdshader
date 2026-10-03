// Contact shadows (ContactShadows): soft darkening on the floor round the foot of walls, columns and
// props, where sky light can't reach in under them. Each card lies flat round one footprint; instance
// custom data gives the footprint's half sizes in metres (red, green), its corner radius (blue) and how
// dark the shadow is against it (alpha). The card reaches past the footprint by the fade distance and
// multiplies what's under it, darkest at the footprint, fading to nothing at its edge.
shader_type spatial;
render_mode blend_mul, unshaded, depth_draw_never, cull_back, fog_disabled, shadows_disabled;

uniform float fade_start = 40.0;
uniform float fade_end = 70.0;

varying vec2 local;
varying vec2 half_size;
varying vec4 foot;

void vertex() {
	half_size = vec2(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz)) * 0.5;
	local = (UV - 0.5) * 2.0 * half_size;
	foot = INSTANCE_CUSTOM;
}

void fragment() {
	// Distance outside the footprint's rounded rectangle, in metres.
	float r = min(foot.z, min(foot.x, foot.y));
	vec2 q = abs(local) - (foot.xy - vec2(r));
	float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
	float reach = max(min(half_size.x - foot.x, half_size.y - foot.y), 0.01);
	float t = 1.0 - smoothstep(0.0, reach, d);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	ALBEDO = vec3(1.0 - foot.w * t * fade);
}
