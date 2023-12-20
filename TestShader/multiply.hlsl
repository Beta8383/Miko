// storage buffer
struct Constant
{
    uint num;
};

[[vk::binding(0)]] ConstantBuffer<Constant> constants;
[[vk::binding(1)]] RWStructuredBuffer<float> data1;

[numthreads(1024, 1, 1)]
void main(uint groupIndex : SV_GroupIndex)
{
    data1[groupIndex] *= constants.num;
}