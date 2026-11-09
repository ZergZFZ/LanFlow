using Xunit;

// ConfigStore 通过环境变量 LANFLOW_CONFIG_DIR 解析配置目录；
// 禁用并行，避免不同测试类同时改写该进程级环境变量。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
