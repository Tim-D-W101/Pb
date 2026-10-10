// The fine patterns pressed into gear (gear.gdshader, viewmodel.gdshader): a diamond knurl on grips, a weave on cloth,
// carbon fibre's twill. UVs are in metres along the surface. Each fades out where it's finer than a pixel or two, so it
// never shimmers. The including shader declares `uniform int pattern` (0 none, 1 knurl, 2 weave, 3 twill).

float gear_tri(float x) {
	return abs(fract(x) - 0.5) * 2.0;
}

// The pattern's height (m) at uv; how much of it shows, and how it darkens the colour in its hollows.
float gear_pattern_height(int kind, vec2 uv, out float shows, out float tone) {
	tone = 1.0;
	shows = 0.0;
	if (kind == 1) {
		float p = 0.0014;
		shows = clamp(1.6 - 1.2 * length(fwidth(uv)) / p, 0.0, 1.0);
		float h = min(gear_tri((uv.x + uv.y) / p), gear_tri((uv.x - uv.y) / p));
		tone = mix(0.8, 1.05, h);
		return h * 0.00025;
	} else if (kind == 2) {
		float p = 0.0016;
		shows = clamp(1.6 - 1.2 * length(fwidth(uv)) / p, 0.0, 1.0);
		vec2 w = uv / p;
		float a = sin(w.x * 6.2832) * 0.5 + 0.5;
		float b = sin(w.y * 6.2832) * 0.5 + 0.5;
		float over = step(0.5, fract(floor(w.x) * 0.5 + floor(w.y) * 0.5));
		float h = mix(a, b, over);
		tone = mix(0.86, 1.06, h);
		return h * 0.0002;
	} else if (kind == 3) {
		float p = 0.003;
		shows = clamp(1.6 - 1.2 * length(fwidth(uv)) / p, 0.0, 1.0);
		vec2 cell = floor(uv / p);
		float flip = mod(cell.x + cell.y, 2.0);
		float t = flip < 0.5 ? uv.x + uv.y : uv.x - uv.y;
		float h = gear_tri(t / (p * 0.5));
		tone = mix(0.62, 1.18, flip) * mix(0.9, 1.05, h);
		return h * 0.00012;
	}
	return 0.0;
}

// Presses pattern `kind` into a fragment: its colour and roughness, and its normal (view space) bumped by the height's
// screen-space slope, so no tangents are needed.
void gear_apply_pattern(int kind, vec2 uv, vec3 vertex, inout vec3 pattern_albedo, inout float pattern_rough, inout vec3 pattern_normal) {
	if (kind == 0) {
		return;
	}

	float shows;
	float tone;
	float h = gear_pattern_height(kind, uv, shows, tone);
	pattern_albedo *= mix(1.0, tone, shows);
	pattern_rough = clamp(pattern_rough - (tone - 1.0) * 0.3 * shows, 0.02, 1.0);
	vec3 dpdx = dFdx(vertex);
	vec3 dpdy = dFdy(vertex);
	vec3 r1 = cross(dpdy, pattern_normal);
	vec3 r2 = cross(pattern_normal, dpdx);
	float det = dot(dpdx, r1);
	vec3 grad = sign(det) * (dFdx(h) * r1 + dFdy(h) * r2);
	vec3 bumped = abs(det) * pattern_normal - grad;
	if (dot(bumped, bumped) > 1e-30) {
		pattern_normal = normalize(mix(pattern_normal, normalize(bumped), shows));
	}
}
