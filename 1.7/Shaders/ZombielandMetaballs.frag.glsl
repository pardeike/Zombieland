#version 450

layout(location = 1) in vec2 vUv;
layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    int _MetaballCount;       // @default = 0
    vec4 _MetaballWorldSize;  // @default = vec4(1.0, 1.0, 0.0, 0.0)
} uMaterial;

struct Metaball
{
    vec4 shape;
    vec4 motion;
    vec4 tint;
};

layout(std430, set = 2, binding = 1) readonly buffer _MetaballBuffer
{
    Metaball balls[];
};

void main()
{
    float field = 0.0;
    vec3 color = vec3(0.0);
    for (int i = 0; i < uMaterial._MetaballCount; i++)
    {
        Metaball ball = balls[i];
        float radius = ball.shape.x * clamp(ball.shape.y, 0.0, 1.0);
        if (radius <= 0.0001)
            continue;

        vec2 delta = (vUv - ball.motion.xy) * max(uMaterial._MetaballWorldSize.xy, vec2(0.0001));
        float contribution = (radius * radius) / max(dot(delta, delta), 0.0001);
        contribution *= contribution;
        contribution *= max(ball.shape.z, 0.0);
        field += contribution;
        color += ball.tint.rgb * contribution;
    }

    float alpha = smoothstep(0.45, 1.20, field) * 0.40;
    float edge = smoothstep(0.45, 1.80, field);
    vec3 body = field > 0.0001 ? color / field : vec3(0.0, 0.8, 0.0);
    body = mix(vec3(0.0), body, edge);
    outColor = vec4(body, alpha);
}
