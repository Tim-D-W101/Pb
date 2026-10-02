// Ripples on the puddles (PuddleRipples): a flat card on the water, the ring spreading from its middle
// to its edge (custom data: r = when it started, on the `now` clock; g = how long it lasts; b = a seed
// for its wobble), fast at first and slowing, widening and fading as it goes. Glossy like the water, so
// the crest catches the sky, with a darker trough just inside it. Instance colour alpha: how strong it starts.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform float now = 0.0;
uniform vec3 crest : source_color = vec3(0.62, 0.64, 0.66);
uniform vec3 trough : source_color = vec3(0.08, 0.08, 0.09);

varying float v_age;
varying float v_life;
varying float v_seed;

void vertex() {
	v_age = now - INSTANCE_CUSTOM.r;
	v_life = max(INSTANCE_CUSTOM.g, 0.01);
	v_seed = INSTANCE_CUSTOM.b;
	// Not started yet, or gone: nothing to draw.
	if (v_age < 0.0 || v_age > v_life) {
		VERTEX *= 0.0;
	}
}

void fragment() {
	float t = clamp(v_age / v_life, 0.0, 1.0);
	vec2 d = UV - 0.5;
	float angle = atan(d.y, d.x);
	// Not quite round: the water's surface and the wind push it about.
	float r = length(d) * 2.0 * (1.0 + 0.04 * sin(angle * 5.0 + v_seed) + 0.03 * sin(angle * 3.0 - v_seed * 1.7));
	float front = sqrt(t);
	float width = 0.07 + 0.12 * t;
	float bright = smoothstep(front - width, front, r) * (1.0 - smoothstep(front, front + width * 0.8, r));
	float dark = smoothstep(front - width * 2.4, front - width, r) * (1.0 - smoothstep(front - width, front - width * 0.3, r));
	float fade = (1.0 - t) * (1.0 - t) * COLOR.a;
	ALBEDO = mix(trough, crest, bright / max(bright + dark, 1e-3));
	ROUGHNESS = 0.12;
	SPECULAR = 0.6;
	ALPHA = clamp(bright + dark * 0.7, 0.0, 1.0) * fade;
}
