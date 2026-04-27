package Api

import "C"
import (
	"bytes"
	"encoding/json"
	"errors"
	"github.com/qtgolang/SunnyNet/Call"
	"github.com/qtgolang/SunnyNet/public"
	redis "github.com/qtgolang/SunnyNet/src/Redis"
	"strings"
	"sync"
)

var RedisMap = make(map[int]interface{})
var RedisL sync.Mutex

const nbsp = "++&nbsp&++"

var ErrorNull = errors.New("")

func DelRedisContext(Context int) {
	RedisL.Lock()
	delete(RedisMap, Context)
	RedisL.Unlock()
}
func LoadRedisContext(Context int) *redis.Redis {
	RedisL.Lock()
	s := RedisMap[Context]
	RedisL.Unlock()
	if s == nil {
		return nil
	}
	return s.(*redis.Redis)
}

func SubCall(msg string, call int, nc bool) {
	if call > 0 {
		if nc {
			go Call.Call(call, msg)
		} else {
			Call.Call(call, msg)
		}
	}
}

// CreateRedis 创建 Redis 对象
func CreateRedis() int {
	w := redis.NewRedis()
	Context := newMessageId()
	w.Context = Context
	RedisL.Lock()
	RedisMap[Context] = w
	RedisL.Unlock()
	return Context
}

// RemoveRedis 释放 Redis 对象
func RemoveRedis(Context int) {
	k := LoadRedisContext(Context)
	if k != nil {
		k.Close()
	}
	DelRedisContext(Context)
}

// RedisDial Redis 连接
func RedisDial(Context int, host, pass string, db, PoolSize, MinIdleCons, DialTimeout, ReadTimeout, WriteTimeout, PoolTimeout, IdleCheckFrequency, IdleTimeout int, error uintptr) bool {
	public.WriteErr(ErrorNull, error)
	w := LoadRedisContext(Context)
	if w == nil {
		return false
	}
	ex := w.Open(
		host,
		pass,
		db,
		PoolSize, MinIdleCons, DialTimeout, ReadTimeout, WriteTimeout, PoolTimeout, IdleCheckFrequency, IdleTimeout)
	if ex != nil {
		public.WriteErr(ex, error)
	}
	return ex == nil
}

// RedisSet Redis 设置值
func RedisSet(Context int, key, val string, expr int) bool {
	w := LoadRedisContext(Context)
	if w == nil {
		return false
	}
	return w.Set(key, val, expr)
}

// RedisSetBytes Redis 设置Bytes值
func RedisSetBytes(Context int, key string, val []byte, expr int) bool {
	w := LoadRedisContext(Context)
	if w == nil {
		return false
	}
	return w.Set(key, val, expr)
}

// RedisSetNx Redis 设置NX 【如果键名存在返回假】
func RedisSetNx(Context int, key, val string, expr int) bool {
	w := LoadRedisContext(Context)
	if w == nil {
		return false
	}
	return w.SetNX(key, val, expr)
}

// RedisExists Redis 检查指定 key 是否存在
func RedisExists(Context int, key string) bool {
	w := LoadRedisContext(Context)
	if w == nil {
		return false
	}
	return w.Exists(key)
}

// RedisGetStr Redis 取文本值
func RedisGetStr(Context int, key string) uintptr {
	w := LoadRedisContext(Context)
	if w == nil {
		return 0
	}
	s := w.GetStr(key)
	if s == "" {
		return 0
	}
	return public.PointerPtr(s)
}

// RedisGetBytes Redis 取文本值
func RedisGetBytes(Context int, key string) uintptr {
	w := LoadRedisContext(Context)
	if w == nil {
		return 0
	}
	s := w.GetBytes(key)
	if len(s) < 1 {
		return 0
	}
	return public.PointerPtr(s)
}

// RedisDo Redis 自定义 执行和查询命令 返回操作结果可能是值 也可能是JSON文本
func RedisDo(Context int, args string, error uintptr) uintptr {
	public.WriteErr(ErrorNull, error)
	w := LoadRedisContext(Context)
	if w == nil {
		public.WriteErr(errors.New("Redis no create 0x002 "), error)
		return 0
	}
	arr := strings.Split(strings.ReplaceAll(args, "\\ ", nbsp), " ")
	var InterFaceArr = make([]interface{}, 0)
	for _, v := range arr {
		if len(v) > 0 {
			InterFaceArr = append(InterFaceArr, strings.ReplaceAll(v, nbsp, " "))
		}
	}
	if len(InterFaceArr) < 1 {
		public.WriteErr(errors.New("Parameter error "), error)
		return 0
	}
	Val, er := w.Client.Do(InterFaceArr...).Result()
	if er != nil {
		public.WriteErr(er, error)
		return 0
	}
	b, er := json.Marshal(Val)
	if er != nil {
		public.WriteErr(er, error)
		return 0
	}
	if len(b) < 1 {
		public.WriteErr(errors.New("The execution succeeds but no data is returned "), error)
		return 0
	}
	return public.PointerPtr(b)
}

// RedisGetKeys Redis 取指定条件键名
func RedisGetKeys(Context int, key string) uintptr {
	w := LoadRedisContext(Context)
	if w == nil {
		return 0
	}
	var b bytes.Buffer
	keys, _ := w.Client.Keys(key).Result()
	for _, v := range keys {
		b.WriteString(v)
		b.WriteByte(0)
	}
	return public.PointerPtr(public.BytesCombine(public.IntToBytes(b.Len()), b.Bytes()))
}

// RedisGetInt Redis 取整数值
func RedisGetInt(Context int, key string) int64 {
	w := LoadRedisContext(Context)
	if w == nil {
		return 0
	}
	return w.GetInt(key)
}

// RedisClose Redis 关闭
func RedisClose(Context int) {
	w := LoadRedisContext(Context)
	if w == nil {
		return
	}
	w.Close()
}

// RedisFlushAll Redis 清空redis服务器
func RedisFlushAll(Context int) {
	//用于清空整个 redis 服务器的数据(删除所有数据库的所有 key )。
	w := LoadRedisContext(Context)
	if w == nil {
		return
	}
	w.FlushAll()
}

// RedisFlushDB Redis 清空当前数据库
func RedisFlushDB(Context int) {
	//用于清空当前数据库中的所有 key。
	w := LoadRedisContext(Context)
	if w == nil {
		return
	}
	w.FlushDB()
}

// RedisDelete Redis 删除
func RedisDelete(Context int, key string) bool {
	w := LoadRedisContext(Context)
	if w == nil {
		return false
	}
	return w.Delete(key)
}

// RedisSubscribe Redis 订阅消息
func RedisSubscribe(Context int, scribe string, call int, nc bool) {
	w := LoadRedisContext(Context)
	if w == nil {
		return
	}
	w.Sub(scribe, call, nc, SubCall)
}
