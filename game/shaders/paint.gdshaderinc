// Paint that has landed on a surface (Phase 5, M5.4; game/ballistics/PaintSlots.cs): up to 16 splats kept in the
// surface's own frame (a character's rest pose, an item of gear's model space, the first-person rig's), each drawn
// from the splat atlas (game/ballistics/WetSplats.cs: red its coverage, green its thickness, blue the shell's flecks)
// projected along the way it faces, so it wraps round an arm or a barrel and moves with it. A surface facing away from
// a splat, or too far in front of or behind it, takes none of it. Fresh paint is wet: glossy, its colour deeper; it
// dries to satin.
uniform int splat_count = 0;
// Each splat: its middle (xyz) and radius (w); the way across it (xyz) and its shape (w, the atlas's cell); the way it
// faces, out of the surface (xyz), and how wet it still is (w, 1 just landed to 0 dry); its colour (rgb, linear) and how
// far behind its middle the surface may lie (w: a ball's break is found on a hitbox round the body, not on the body).
uniform vec4 splat_at[16];
uniform vec4 splat_across[16];
uniform vec4 splat_facing[16];
uniform vec4 splat_colour[16];
uniform sampler2D splat_atlas : hint_default_transparent, filter_linear_mipmap, repeat_disable;
uniform float splat_wet_roughness = 0.07;
uniform float splat_dry_roughness = 0.55;
uniform float splat_wet_darken = 0.12;
uniform float splat_bump = 1.0;

// Paints the fragment at p (the surface's frame), whose surface faces n there: its colour and roughness, how much paint
// covers it, and its view-space normal bumped by the paint's thickness (vertex: the fragment's view-space position).
float splat_apply(vec3 p, vec3 n, vec3 vertex, inout vec3 splat_albedo_io, inout float splat_rough_io, inout vec3 splat_normal_io) {
	if (splat_count <= 0) {
		return 0.0;
	}

	vec3 dpx = dFdx(p);
	vec3 dpy = dFdy(p);
	vec3 facing_n = normalize(n);
	float height = 0.0;
	float covered = 0.0;
	for (int i = 0; i < splat_count; i++) {
		vec4 at = splat_at[i];
		if (at.w <= 0.0) {
			continue;
		}

		vec3 d = p - at.xyz;
		vec3 f = splat_facing[i].xyz;
		float h = dot(d, f);
		if (h > at.w * 0.4 || h < -splat_colour[i].w) {
			continue;
		}

		float facing = dot(facing_n, f);
		if (facing < 0.05) {
			continue;
		}

		vec3 a = splat_across[i].xyz;
		vec3 b = cross(f, a);
		float across = 0.5 / at.w;
		vec2 uv = vec2(dot(d, a), dot(d, b)) * across + 0.5;
		if (uv.x <= 0.0 || uv.y <= 0.0 || uv.x >= 1.0 || uv.y >= 1.0) {
			continue;
		}

		// The shape's cell in the atlas (4 across, 2 down), sampled with its own slopes, so it filters right in a loop.
		int cell = int(splat_across[i].w + 0.5);
		vec2 scale = vec2(0.25, 0.5);
		vec2 cell_uv = (vec2(float(cell % 4), float(cell / 4)) + uv) * scale;
		vec2 gx = vec2(dot(dpx, a), dot(dpx, b)) * across * scale;
		vec2 gy = vec2(dot(dpy, a), dot(dpy, b)) * across * scale;
		vec4 s = textureGrad(splat_atlas, cell_uv, gx, gy);
		float cover = s.r * smoothstep(0.05, 0.35, facing);
		if (cover <= 0.002) {
			continue;
		}

		float wet = splat_facing[i].w;
		vec3 colour = splat_colour[i].rgb * (1.0 - splat_wet_darken * wet) * (0.9 - 0.12 * s.g + 0.22 * s.b);
		splat_albedo_io = mix(splat_albedo_io, colour, cover);
		float thick = clamp(s.g * 1.6, 0.0, 1.0);
		float wet_rough = mix(0.18, splat_wet_roughness, thick);
		float dry_rough = mix(0.42, splat_dry_roughness, thick);
		splat_rough_io = mix(splat_rough_io, mix(dry_rough, wet_rough, wet) + 0.12 * s.b, cover);
		height = max(height, s.g * cover);
		covered = max(covered, cover);
	}

	// Its thickness lifts the surface in the light: the height's slope on the screen, as gear_patterns.gdshaderinc does.
	float lift = height * 0.0006 * splat_bump;
	vec3 vx = dFdx(vertex);
	vec3 vy = dFdy(vertex);
	vec3 r1 = cross(vy, splat_normal_io);
	vec3 r2 = cross(splat_normal_io, vx);
	float det = dot(vx, r1);
	vec3 grad = sign(det) * (dFdx(lift) * r1 + dFdy(lift) * r2);
	vec3 bumped = abs(det) * splat_normal_io - grad;
	if (covered > 0.0 && dot(bumped, bumped) > 1e-30) {
		splat_normal_io = normalize(bumped);
	}

	return covered;
}
