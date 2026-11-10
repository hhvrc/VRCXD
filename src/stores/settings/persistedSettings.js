import { ref } from 'vue';

import configRepository from '../../services/config';

const codecs = {
    bool: {
        read: (key, defaultValue) => configRepository.getBool(key, defaultValue),
        write: (key, value) => configRepository.setBool(key, value)
    },
    string: {
        read: (key, defaultValue) => configRepository.getString(key, defaultValue),
        write: (key, value) => configRepository.setString(key, value)
    },
    int: {
        read: (key, defaultValue) => configRepository.getInt(key, defaultValue),
        write: (key, value) => configRepository.setInt(key, value)
    },
    float: {
        read: (key, defaultValue) => configRepository.getFloat(key, defaultValue),
        write: (key, value) => configRepository.setFloat(key, value)
    },
    json: {
        read: async (key, defaultValue) =>
            JSON.parse(await configRepository.getString(key, JSON.stringify(defaultValue))),
        write: (key, value) => configRepository.setString(key, JSON.stringify(value))
    }
};

/**
 * @param {string} name
 * @returns {string}
 */
function defaultSetterName(name) {
    return `set${name.charAt(0).toUpperCase()}${name.slice(1)}`;
}

/**
 * Declares settings that are a plain ref backed by one configRepository key.
 *
 * Each spec entry is `name: [configKey, type, defaultValue, options?]` where type is one of 'bool', 'string', 'int',
 * 'float' or 'json' ('json' is stored as a string and parsed on load).
 *
 * - A ref is created per entry. It holds `options.initial` (if given) or `defaultValue` until `load()` has read the
 *   stored value.
 * - A setter is generated per entry, named `set<Name>` unless `options.setter` gives another name, or is `false` to
 *   generate none. Bool setters toggle the current value and ignore their arguments; the others take the new value.
 *   Both store it and return the write promise.
 * - `load()` reads every entry in one Promise.all and assigns the refs.
 *
 * A setting whose setter has side effects can still be declared here with `setter: false` and get a hand-written setter
 * in the store.
 *
 * @param {Record<
 *     string,
 *     [string, 'bool' | 'string' | 'int' | 'float' | 'json', any, { initial?: any; setter?: string | false }?]
 * >} specs
 */
export function definePersistedSettings(specs) {
    const entries = Object.entries(specs).map(([name, [key, type, defaultValue, options = {}]]) => {
        const codec = codecs[type];
        if (!codec) {
            throw new Error(`Unknown setting type "${type}" for ${name}`);
        }
        const value = ref('initial' in options ? options.initial : defaultValue);
        return { name, key, codec, defaultValue, options, value, type };
    });

    /** @type {Record<string, import('vue').Ref<any>>} */
    const refs = {};
    /** @type {Record<string, (value?: any) => Promise<void>>} */
    const setters = {};
    for (const { name, key, codec, options, value, type } of entries) {
        refs[name] = value;
        const setterName = options.setter ?? defaultSetterName(name);
        if (!setterName) {
            continue;
        }
        setters[setterName] =
            type === 'bool'
                ? () => {
                      value.value = !value.value;
                      return codec.write(key, value.value);
                  }
                : (next) => {
                      value.value = next;
                      return codec.write(key, value.value);
                  };
    }

    async function load() {
        const values = await Promise.all(entries.map(({ key, codec, defaultValue }) => codec.read(key, defaultValue)));
        entries.forEach(({ value }, i) => {
            value.value = values[i];
        });
    }

    return { refs, setters, load };
}
