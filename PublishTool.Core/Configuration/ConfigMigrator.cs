namespace PublishTool.Configuration;

public interface IConfigMigrator
{
    public int From { get; }
    public object Migrate(object source);
}

public abstract class ConfigMigrator<TFrom, TTo> : IConfigMigrator where TFrom : class where TTo : class
{
    public abstract int From { get; }

    public abstract TTo Migrate(TFrom source);

    public object Migrate(object source) => Migrate(source as TFrom
        ?? throw new InvalidOperationException(
            $"Config migrator from version {From} expected a {typeof(TFrom).Name} but received {source.GetType().Name}."));
}
